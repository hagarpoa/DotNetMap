using System.Text;
using System.Text.Json;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Store;
using Microsoft.Data.Sqlite;

namespace DotNetMap.Core.Analysis;

/// <summary>
/// Index-only hotspots for prioritization (DNM-023): large methods, high outbound calls, high fan-in.
/// </summary>
public static class Hotspots
{
    public enum Metric
    {
        Size,
        Calls,
        FanIn,
        Types
    }

    public sealed record MemberHit(
        string Id,
        string Name,
        string Kind,
        string ParentType,
        string? Signature,
        string? File,
        int? StartLine,
        int? EndLine,
        int LineCount,
        int SizeChars,
        int Score,
        string ScoreLabel);

    public sealed record TypeHit(
        string FullName,
        string Kind,
        int MemberCount,
        int SizeChars,
        string? File,
        int? StartLine,
        int? EndLine);

    public sealed record Result(
        Metric Metric,
        int Limit,
        IReadOnlyList<MemberHit> Members,
        IReadOnlyList<TypeHit> Types,
        string Note);

    public static Result Compute(MapStore store, Metric metric = Metric.Size, int limit = 15)
    {
        limit = Math.Clamp(limit, 1, 50);
        return metric switch
        {
            Metric.Types => new Result(metric, limit, [], TopTypes(store, limit),
                "Types ranked by member count (then size)."),
            Metric.Calls => new Result(metric, limit, TopMembersByCalls(store, limit), [],
                "Methods ranked by outbound solution-local call count (index)."),
            Metric.FanIn => new Result(metric, limit, TopMembersByFanIn(store, limit), [],
                "Members ranked by stored consumers_json site count (run callers --update to populate)."),
            _ => new Result(Metric.Size, limit, TopMembersBySize(store, limit), [],
                "Methods ranked by line count (then size_chars).")
        };
    }

    public static string Format(Result result, string format = "md")
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                metric = result.Metric.ToString().ToLowerInvariant(),
                limit = result.Limit,
                note = result.Note,
                members = result.Members,
                types = result.Types
            }, JsonOpts);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Hotspots by {result.Metric.ToString().ToLowerInvariant()}");
        sb.AppendLine();
        sb.AppendLine(result.Note);
        sb.AppendLine();

        if (result.Metric == Metric.Types)
        {
            var i = 1;
            foreach (var t in result.Types)
            {
                var loc = t.File is null ? "" : t.StartLine is int l ? $" @ `{t.File}:L{l}`" : $" @ `{t.File}`";
                sb.AppendLine($"{i}. **{t.FullName}** ({t.Kind}) — {t.MemberCount} members, {t.SizeChars} chars{loc}");
                i++;
            }
        }
        else
        {
            var i = 1;
            foreach (var m in result.Members)
            {
                var loc = m.File is null ? "" : m.StartLine is int l
                    ? $" @ `{m.File}:L{l}-{m.EndLine}`"
                    : $" @ `{m.File}`";
                sb.AppendLine(
                    $"{i}. `{m.ParentType}.{m.Name}` ({m.Kind}) — **{m.ScoreLabel}**{loc}");
                i++;
            }
        }

        if (result.Members.Count == 0 && result.Types.Count == 0)
            sb.AppendLine("_No hotspots found._");

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        return sb.ToString();
    }

    public static Metric ParseMetric(string? value) =>
        (value ?? "size").Trim().ToLowerInvariant() switch
        {
            "calls" or "call" or "fanout" => Metric.Calls,
            "fanin" or "fan-in" or "consumers" or "refs" => Metric.FanIn,
            "types" or "type" => Metric.Types,
            _ => Metric.Size
        };

    private static List<MemberHit> TopMembersBySize(MapStore store, int limit)
    {
        using var cmd = store.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.name, m.kind, m.signature, t.full_name, f.relative_path,
                   m.start_line, m.end_line, m.size_chars
            FROM members m
            INNER JOIN types t ON t.id = m.type_id
            LEFT JOIN source_files f ON f.id = m.file_id
            WHERE m.kind IN ('method', 'constructor')
            ORDER BY
              CASE
                WHEN m.start_line IS NOT NULL AND m.end_line IS NOT NULL AND m.end_line >= m.start_line
                THEN m.end_line - m.start_line + 1
                ELSE m.size_chars
              END DESC,
              m.size_chars DESC
            LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$n", limit);
        return ReadMemberHits(cmd, scoreFromLines: true);
    }

    private static List<MemberHit> TopMembersByCalls(MapStore store, int limit)
    {
        // Load candidates then rank in memory (JSON deps)
        using var cmd = store.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.name, m.kind, m.signature, t.full_name, f.relative_path,
                   m.start_line, m.end_line, m.size_chars, m.dependencies_json
            FROM members m
            INNER JOIN types t ON t.id = m.type_id
            LEFT JOIN source_files f ON f.id = m.file_id
            WHERE m.kind IN ('method', 'constructor');
            """;

        var rows = new List<(MemberHit Base, int Calls)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var start = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
            var end = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
            var size = reader.GetInt32(8);
            var lineCount = start is int s && end is int e && e >= s ? e - s + 1 : 0;
            var deps = reader.IsDBNull(9) ? "[]" : reader.GetString(9);
            var callCount = RelationPresentation.CallsOnly(RelationPresentation.Parse(deps)).Count;
            if (callCount == 0)
                continue;

            rows.Add((new MemberHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(4),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                start, end, lineCount, size,
                callCount,
                $"{callCount} calls"), callCount));
        }

        return rows.OrderByDescending(r => r.Calls)
            .Take(limit)
            .Select(r => r.Base)
            .ToList();
    }

    private static List<MemberHit> TopMembersByFanIn(MapStore store, int limit)
    {
        using var cmd = store.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.name, m.kind, m.signature, t.full_name, f.relative_path,
                   m.start_line, m.end_line, m.size_chars, m.consumers_json
            FROM members m
            INNER JOIN types t ON t.id = m.type_id
            LEFT JOIN source_files f ON f.id = m.file_id;
            """;

        var rows = new List<(MemberHit Base, int FanIn)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var start = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
            var end = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
            var size = reader.GetInt32(8);
            var lineCount = start is int s && end is int e && e >= s ? e - s + 1 : 0;
            var cons = reader.IsDBNull(9) ? "[]" : reader.GetString(9);
            var fanIn = RelationPresentation.Parse(cons).Count;
            if (fanIn == 0)
                continue;

            rows.Add((new MemberHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(4),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                start, end, lineCount, size,
                fanIn,
                $"{fanIn} refs"), fanIn));
        }

        return rows.OrderByDescending(r => r.FanIn)
            .Take(limit)
            .Select(r => r.Base)
            .ToList();
    }

    private static List<TypeHit> TopTypes(MapStore store, int limit)
    {
        using var cmd = store.CreateCommand();
        cmd.CommandText = """
            SELECT t.full_name, t.kind, t.size_chars, f.relative_path, t.start_line, t.end_line,
                   (SELECT COUNT(*) FROM members m WHERE m.type_id = t.id) AS mc
            FROM types t
            LEFT JOIN source_files f ON f.id = t.file_id
            ORDER BY mc DESC, t.size_chars DESC
            LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$n", limit);
        var list = new List<TypeHit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TypeHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(6),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return list;
    }

    private static List<MemberHit> ReadMemberHits(SqliteCommand cmd, bool scoreFromLines)
    {
        var list = new List<MemberHit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var start = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
            var end = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
            var size = reader.GetInt32(8);
            var lineCount = start is int s && end is int e && e >= s ? e - s + 1 : 0;
            var score = scoreFromLines && lineCount > 0 ? lineCount : size;
            var label = lineCount > 0 ? $"{lineCount} lines" : $"{size} chars";
            list.Add(new MemberHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(4),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                start, end, lineCount, size,
                score,
                label));
        }

        return list;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
