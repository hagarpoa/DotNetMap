using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetMap.Core.Domain;
using NetMap.Core.Store;

namespace NetMap.Core.Export;

public sealed class ExportOptions
{
    public int MaxTypes { get; init; } = 200;
    public int MaxMembersPerType { get; init; } = 50;
    public bool IncludeMembers { get; init; }
    public bool IncludeDeps { get; init; } = true;
}

public static class CompactExporter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToMarkdown(MapStore store, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var s = store.GetStatus();
        var sb = new StringBuilder();
        sb.AppendLine($"# NetMap: {s.SolutionName}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{s.SolutionPath}`");
        sb.AppendLine($"- Indexed (UTC): {s.IndexedAtUtc:u}");
        sb.AppendLine($"- Mode: `{s.IndexMode}`");
        sb.AppendLine($"- Projects: {s.ProjectCount} | Types: {s.TypeCount} | Members: {s.MemberCount} | Files: {s.FileCount}");
        sb.AppendLine($"- Token estimate (overview): ~{s.TokenEstimateOverview}");
        sb.AppendLine();
        sb.AppendLine("## Types");
        sb.AppendLine();

        foreach (var t in store.ListTypes(options.MaxTypes))
        {
            sb.Append($"- **{t.FullName}** ({t.Kind}, {t.Accessibility}, {t.MemberCount} members)");
            if (!string.IsNullOrEmpty(t.Summary))
                sb.Append($" — {t.Summary}");
            sb.AppendLine();

            if (options.IncludeMembers)
            {
                var detail = store.GetTypeDetail(t.FullName, options.MaxMembersPerType);
                if (detail is null)
                    continue;

                if (options.IncludeDeps && detail.DependenciesJson is not "[]" and not "")
                    sb.AppendLine($"  - deps: `{Truncate(detail.DependenciesJson, 200)}`");

                foreach (var m in detail.Members)
                {
                    sb.Append($"  - `{m.Signature}`");
                    if (!string.IsNullOrEmpty(m.Summary))
                        sb.Append($" — {m.Summary}");
                    sb.AppendLine();
                }
            }
        }

        if (s.TypeCount > options.MaxTypes)
            sb.AppendLine().AppendLine($"_… and {s.TypeCount - options.MaxTypes} more types_");

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        return sb.ToString();
    }

    public static string ToJson(MapStore store, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var s = store.GetStatus();

        object typesPayload;
        if (options.IncludeMembers)
        {
            typesPayload = store.ListTypes(options.MaxTypes).Select(t =>
            {
                var d = store.GetTypeDetail(t.FullName, options.MaxMembersPerType);
                return new
                {
                    t.FullName,
                    t.Kind,
                    t.Accessibility,
                    t.Summary,
                    t.MemberCount,
                    t.TokenEstimate,
                    path = d?.RelativePath,
                    lines = d is null ? null : new { start = d.StartLine, end = d.EndLine },
                    dependenciesJson = options.IncludeDeps ? d?.DependenciesJson : null,
                    members = d?.Members.Select(m => new
                    {
                        m.Name,
                        m.Kind,
                        m.Signature,
                        m.Summary,
                        m.ReturnType,
                        m.TokenEstimate,
                        lines = new { start = m.StartLine, end = m.EndLine }
                    })
                };
            }).ToList();
        }
        else
        {
            typesPayload = store.ListTypes(options.MaxTypes).Select(t => new
            {
                t.FullName,
                t.Kind,
                t.Accessibility,
                t.Summary,
                t.MemberCount,
                t.TokenEstimate
            }).ToList();
        }

        var payload = new
        {
            solution = s.SolutionName,
            path = s.SolutionPath,
            mode = s.IndexMode,
            indexedAtUtc = s.IndexedAtUtc,
            counts = new
            {
                projects = s.ProjectCount,
                types = s.TypeCount,
                members = s.MemberCount,
                files = s.FileCount
            },
            tokenEstimate = s.TokenEstimateOverview,
            types = typesPayload
        };

        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    public static string SearchToMarkdown(IReadOnlyList<SearchHit> hits, string query)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Search: {query}");
        sb.AppendLine();
        sb.AppendLine($"Hits: {hits.Count}");
        sb.AppendLine();
        foreach (var h in hits)
        {
            if (h.Category == "type")
            {
                sb.Append($"- **[type]** `{h.Display ?? h.Name}`");
                if (!string.IsNullOrEmpty(h.Summary))
                    sb.Append($" — {h.Summary}");
            }
            else
            {
                sb.Append($"- **[member]** `{h.ParentType}.{h.Name}`");
                if (!string.IsNullOrEmpty(h.Display))
                    sb.Append($" — `{h.Display}`");
                if (!string.IsNullOrEmpty(h.Summary))
                    sb.Append($" — {h.Summary}");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        return sb.ToString();
    }

    public static string SearchToJson(IReadOnlyList<SearchHit> hits, string query) =>
        JsonSerializer.Serialize(new { query, count = hits.Count, hits }, JsonOpts);

    public static string TypeDetailToMarkdown(TypeDetail t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {t.FullName}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: `{t.Kind}` | Access: `{t.Accessibility}`");
        if (!string.IsNullOrEmpty(t.RelativePath))
            sb.AppendLine($"- File: `{t.RelativePath}` L{t.StartLine}-{t.EndLine} ({t.SizeChars} chars)");
        if (!string.IsNullOrEmpty(t.Summary))
            sb.AppendLine($"- Summary: {t.Summary}");
        if (t.DependenciesJson is not "[]" and not "")
            sb.AppendLine($"- Deps: `{Truncate(t.DependenciesJson, 400)}`");
        if (t.ConsumersJson is not "[]" and not "")
            sb.AppendLine($"- Consumers: `{Truncate(t.ConsumersJson, 400)}`");
        sb.AppendLine($"- Token estimate: ~{t.TokenEstimate}");
        sb.AppendLine();
        sb.AppendLine("## Members");
        sb.AppendLine();
        foreach (var m in t.Members)
        {
            sb.Append($"- `{m.Signature}`");
            if (m.StartLine is not null)
                sb.Append($"  _{m.StartLine}-{m.EndLine}_");
            if (!string.IsNullOrEmpty(m.Summary))
                sb.Append($" — {m.Summary}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"_~{TokenEstimator.FromText(sb.ToString())} tokens_");
        return sb.ToString();
    }

    public static string TypeDetailToJson(TypeDetail t) =>
        JsonSerializer.Serialize(t, JsonOpts);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
