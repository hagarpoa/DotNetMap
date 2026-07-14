using System.Text;
using System.Text.Json;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// Index quality metrics for AI trust and coverage (DNM-022).
/// </summary>
public static class IndexQuality
{
    public sealed class Report
    {
        public int TypeCount { get; init; }
        public int TypesWithSummary { get; init; }
        public double TypesWithSummaryPercent { get; init; }
        public int MemberCount { get; init; }
        public int MethodsCount { get; init; }
        public int MethodsWithCalls { get; init; }
        public double MethodsWithCallsPercent { get; init; }
        public int MembersWithConsumers { get; init; }
        public int TypesWithConsumers { get; init; }
        public double AvgMembersPerType { get; init; }
        public double AvgMethodLines { get; init; }
        public int MethodsMissingLocation { get; init; }
        public int TypesMissingLocation { get; init; }
        public int PublicTypes { get; init; }
        public string Grade { get; init; } = "n/a";
        public IReadOnlyList<string> Notes { get; init; } = [];
    }

    public static Report Compute(MapStore store)
    {
        var typeCount = store.ScalarCount("SELECT COUNT(*) FROM types");
        var typesWithSummary = store.ScalarCount(
            "SELECT COUNT(*) FROM types WHERE summary IS NOT NULL AND length(trim(summary)) > 0");
        var typesMissingLoc = store.ScalarCount(
            "SELECT COUNT(*) FROM types WHERE start_line IS NULL OR file_id IS NULL");
        var publicTypes = store.ScalarCount(
            "SELECT COUNT(*) FROM types WHERE accessibility = 'public'");
        var typesWithConsumers = store.ScalarCount(
            "SELECT COUNT(*) FROM types WHERE consumers_json IS NOT NULL AND consumers_json != '[]'");

        var memberCount = store.ScalarCount("SELECT COUNT(*) FROM members");
        var methodsCount = store.ScalarCount(
            "SELECT COUNT(*) FROM members WHERE kind IN ('method', 'constructor')");
        var methodsMissingLoc = store.ScalarCount(
            "SELECT COUNT(*) FROM members WHERE kind IN ('method', 'constructor') AND (start_line IS NULL OR file_id IS NULL)");
        var membersWithConsumers = store.ScalarCount(
            "SELECT COUNT(*) FROM members WHERE consumers_json IS NOT NULL AND consumers_json != '[]'");

        // Methods with at least one outbound call (index)
        var methodsWithCalls = 0;
        var methodLinesSum = 0;
        var methodLinesN = 0;
        using (var cmd = store.CreateCommand())
        {
            cmd.CommandText = """
                SELECT dependencies_json, start_line, end_line
                FROM members
                WHERE kind IN ('method', 'constructor');
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var deps = reader.IsDBNull(0) ? "[]" : reader.GetString(0);
                if (RelationPresentation.CallsOnly(RelationPresentation.Parse(deps)).Count > 0)
                    methodsWithCalls++;

                if (!reader.IsDBNull(1) && !reader.IsDBNull(2))
                {
                    var s = reader.GetInt32(1);
                    var e = reader.GetInt32(2);
                    if (e >= s)
                    {
                        methodLinesSum += e - s + 1;
                        methodLinesN++;
                    }
                }
            }
        }

        var notes = new List<string>();
        if (typesWithSummary == 0 && typeCount > 0)
            notes.Add("No XML summaries indexed — documentation coverage is low.");
        if (methodsWithCalls == 0 && methodsCount > 0)
            notes.Add("No outbound calls stored — reindex with current tool version.");
        if (membersWithConsumers == 0)
            notes.Add("No member consumers stored — run callers --update on key APIs for fan-in.");
        if (methodsMissingLoc > 0)
            notes.Add($"{methodsMissingLoc} methods lack source location.");

        var summaryPct = typeCount == 0 ? 0 : 100.0 * typesWithSummary / typeCount;
        var callsPct = methodsCount == 0 ? 0 : 100.0 * methodsWithCalls / methodsCount;
        var avgMembers = typeCount == 0 ? 0 : (double)memberCount / typeCount;
        var avgLines = methodLinesN == 0 ? 0 : (double)methodLinesSum / methodLinesN;

        var grade = GradeFrom(summaryPct, callsPct, methodsMissingLoc, typeCount);

        return new Report
        {
            TypeCount = typeCount,
            TypesWithSummary = typesWithSummary,
            TypesWithSummaryPercent = Math.Round(summaryPct, 1),
            MemberCount = memberCount,
            MethodsCount = methodsCount,
            MethodsWithCalls = methodsWithCalls,
            MethodsWithCallsPercent = Math.Round(callsPct, 1),
            MembersWithConsumers = membersWithConsumers,
            TypesWithConsumers = typesWithConsumers,
            AvgMembersPerType = Math.Round(avgMembers, 1),
            AvgMethodLines = Math.Round(avgLines, 1),
            MethodsMissingLocation = methodsMissingLoc,
            TypesMissingLocation = typesMissingLoc,
            PublicTypes = publicTypes,
            Grade = grade,
            Notes = notes
        };
    }

    public static string FormatMarkdown(Report q)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Quality");
        sb.AppendLine($"- Grade: **{q.Grade}**");
        sb.AppendLine($"- Types with summary: {q.TypesWithSummary}/{q.TypeCount} ({q.TypesWithSummaryPercent}%)");
        sb.AppendLine($"- Public types: {q.PublicTypes}");
        sb.AppendLine($"- Avg members/type: {q.AvgMembersPerType}");
        sb.AppendLine($"- Methods with outbound calls: {q.MethodsWithCalls}/{q.MethodsCount} ({q.MethodsWithCallsPercent}%)");
        sb.AppendLine($"- Avg method lines: {q.AvgMethodLines}");
        sb.AppendLine($"- Members with stored consumers: {q.MembersWithConsumers}");
        sb.AppendLine($"- Types with stored consumers: {q.TypesWithConsumers}");
        sb.AppendLine($"- Missing locations: types={q.TypesMissingLocation}, methods={q.MethodsMissingLocation}");
        if (q.Notes.Count > 0)
        {
            sb.AppendLine("- Notes:");
            foreach (var n in q.Notes)
                sb.AppendLine($"  - {n}");
        }

        return sb.ToString();
    }

    public static string FormatJson(Report q) =>
        JsonSerializer.Serialize(q, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

    private static string GradeFrom(double summaryPct, double callsPct, int missingLoc, int typeCount)
    {
        if (typeCount == 0)
            return "empty";

        var score = 0;
        if (summaryPct >= 40) score += 2;
        else if (summaryPct >= 15) score += 1;

        if (callsPct >= 30) score += 2;
        else if (callsPct >= 10) score += 1;

        if (missingLoc == 0) score += 2;
        else if (missingLoc < typeCount) score += 1;

        return score switch
        {
            >= 5 => "A",
            >= 4 => "B",
            >= 2 => "C",
            _ => "D"
        };
    }
}
