using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetMap.Core.Domain;
using DotNetMap.Core.Source;
using DotNetMap.Core.Store;

namespace DotNetMap.Core.Export;

public sealed class ExportOptions
{
    public int MaxTypes { get; init; } = 200;
    public int MaxMembersPerType { get; init; } = 50;
    public bool IncludeMembers { get; init; }
    public bool IncludeDeps { get; init; } = true;
    public DetailLevel Detail { get; init; } = DetailLevel.Compact;
    public int MaxChars { get; init; } = OutputLimits.DefaultMaxChars;
    public int MaxRelations { get; init; } = OutputLimits.DefaultMaxRelations;

    /// <summary>When true, append on-demand source snippet (not stored in DB).</summary>
    public bool IncludeSnippet { get; init; }

    public int SnippetContextLines { get; init; }

    public int SnippetMaxChars { get; init; } = 4_000;

    /// <summary>Solution path for path allowlist when reading snippets.</summary>
    public string? SolutionPath { get; init; }

    /// <summary>Optional absolute path resolver (e.g. from MapStore).</summary>
    public Func<string?, string?>? ResolveAbsolutePath { get; init; }
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
        sb.AppendLine($"# DotNetMap: {s.SolutionName}");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{s.SolutionPath}`");
        sb.AppendLine($"- Indexed (UTC): {s.IndexedAtUtc:u}");
        sb.AppendLine($"- Mode: `{s.IndexMode}` | Detail: `{options.Detail.ToString().ToLowerInvariant()}`");
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

                if (options.IncludeDeps)
                    AppendTypeDeps(sb, detail.DependenciesJson, options, indent: "  ");

                foreach (var m in detail.Members)
                    AppendMemberLine(sb, m, options);
            }
        }

        if (s.TypeCount > options.MaxTypes)
            sb.AppendLine().AppendLine($"_… and {s.TypeCount - options.MaxTypes} more types_");

        return Finalize(sb, options.MaxChars);
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
                    lines = d is null ? null : new { start = d.StartLine, end = d.EndLine, count = d?.LineCount },
                    relations = d is null || !options.IncludeDeps
                        ? null
                        : ShapeRelations(d.DependenciesJson, d.ConsumersJson, options),
                    members = d?.Members.Select(m => ShapeMember(m, options))
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
            detail = options.Detail.ToString().ToLowerInvariant(),
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

        return CapJson(JsonSerializer.Serialize(payload, JsonOpts), options.MaxChars);
    }

    public static string SearchToMarkdown(IReadOnlyList<SearchHit> hits, string query, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
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
                if (!string.IsNullOrEmpty(h.Summary) && options.Detail == DetailLevel.Full)
                    sb.Append($" — {h.Summary}");
            }
            else if (h.Category == "body")
            {
                var loc = h.RelativePath ?? h.Display ?? h.Name;
                if (h.Line is int ln)
                    loc = $"{loc}:L{ln}";
                sb.Append($"- **[body]** `{loc}`");
                if (!string.IsNullOrEmpty(h.Snippet))
                    sb.Append($" — `{ShortSig(h.Snippet)}`");
            }
            else
            {
                sb.Append($"- **[member]** `{h.ParentType}.{h.Name}`");
                if (!string.IsNullOrEmpty(h.Display))
                    sb.Append($" — `{ShortSig(h.Display)}`");
            }
            sb.AppendLine();
        }

        return Finalize(sb, options.MaxChars);
    }

    public static string SearchToJson(IReadOnlyList<SearchHit> hits, string query, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var json = JsonSerializer.Serialize(new { query, count = hits.Count, detail = options.Detail.ToString().ToLowerInvariant(), hits }, JsonOpts);
        return CapJson(json, options.MaxChars);
    }

    public static string TypeDetailToMarkdown(TypeDetail t, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var sb = new StringBuilder();
        sb.AppendLine($"# {t.FullName}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: `{t.Kind}` | Access: `{t.Accessibility}`"
                      + (t.IsGenerated ? " | **generated**" : ""));
        var locs = t.AllLocations;
        if (locs.Count > 1)
        {
            sb.AppendLine($"- Locations ({locs.Count} partials):");
            foreach (var loc in locs)
            {
                var path = loc.RelativePath ?? loc.FileId ?? "?";
                var primary = loc.IsPrimary ? " **primary**" : "";
                sb.AppendLine($"  - `{path}` L{loc.StartLine}-{loc.EndLine} ({loc.LineCount} lines){primary}");
            }
        }
        else if (!string.IsNullOrEmpty(t.RelativePath))
        {
            sb.AppendLine($"- File: `{t.RelativePath}` L{t.StartLine}-{t.EndLine} ({t.LineCount} lines)");
        }

        if (!string.IsNullOrEmpty(t.Summary))
            sb.AppendLine($"- Summary: {t.Summary}");

        AppendTypeDeps(sb, t.DependenciesJson, options, indent: "");
        if (options.Detail == DetailLevel.Full || RelationPresentation.Parse(t.ConsumersJson).Count > 0)
        {
            var cons = RelationPresentation.Slice(t.ConsumersJson, options.Detail, options.MaxRelations);
            if (cons.ConsumerNames.Count > 0)
            {
                sb.AppendLine($"- Consumers ({cons.ConsumersTotal}): {string.Join(", ", cons.ConsumerNames.Select(n => $"`{n}`"))}"
                              + (cons.ConsumersTruncated ? " …" : ""));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Members");
        sb.AppendLine();
        foreach (var m in t.Members)
            AppendMemberLine(sb, m, options);

        if (options.IncludeSnippet)
            AppendSnippetMarkdown(sb, t.RelativePath, t.StartLine, t.EndLine, options);

        return Finalize(sb, options.MaxChars);
    }

    public static string TypeDetailToJson(TypeDetail t, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var snippet = options.IncludeSnippet
            ? TrySnippet(t.RelativePath, t.StartLine, t.EndLine, options)
            : null;
        var locs = t.AllLocations;
        var payload = new
        {
            t.Id,
            t.FullName,
            t.Kind,
            t.Accessibility,
            t.Summary,
            t.RelativePath,
            isGenerated = t.IsGenerated,
            lines = new { start = t.StartLine, end = t.EndLine, count = t.LineCount },
            locations = locs.Count > 0
                ? locs.Select(l => new
                {
                    file = l.RelativePath ?? l.FileId,
                    startLine = l.StartLine,
                    endLine = l.EndLine,
                    lineCount = l.LineCount,
                    sizeChars = l.SizeChars,
                    isPrimary = l.IsPrimary
                }).ToList()
                : null,
            detail = options.Detail.ToString().ToLowerInvariant(),
            relations = ShapeRelations(t.DependenciesJson, t.ConsumersJson, options),
            members = t.Members.Select(m => ShapeMember(m, options)),
            snippet = snippet is null ? null : ShapeSnippet(snippet),
            tokenEstimate = t.TokenEstimate
        };
        return CapJson(JsonSerializer.Serialize(payload, JsonOpts), options.MaxChars);
    }

    public static string MemberDetailToMarkdown(MemberDetail m, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var sb = new StringBuilder();
        var title = string.IsNullOrEmpty(m.ParentTypeFullName) ? m.Name : $"{m.ParentTypeFullName}.{m.Name}";
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine($"- Kind: `{m.Kind}` | Access: `{m.Accessibility}`");
        sb.AppendLine($"- Signature: `{ShortSig(m.Signature)}`");
        if (options.Detail == DetailLevel.Full && !string.IsNullOrEmpty(m.ReturnType))
            sb.AppendLine($"- Returns: `{m.ReturnType}`");
        if (!string.IsNullOrEmpty(m.RelativePath))
            sb.AppendLine($"- File: `{m.RelativePath}` L{m.StartLine}-{m.EndLine} ({m.LineCount} lines)");
        else if (m.StartLine is not null)
            sb.AppendLine($"- Lines: L{m.StartLine}-{m.EndLine} ({m.LineCount} lines)");
        if (!string.IsNullOrEmpty(m.Summary))
            sb.AppendLine($"- Summary: {m.Summary}");

        var slice = RelationPresentation.Slice(m.DependenciesJson, options.Detail, options.MaxRelations);
        if (slice.CallNames.Count > 0)
        {
            sb.AppendLine($"- Calls ({slice.CallsTotal}): {string.Join(", ", slice.CallNames.Select(n => $"`{n}`"))}"
                          + (slice.CallsTruncated ? " …" : ""));
        }
        else
        {
            sb.AppendLine("- Calls (0): _(none in solution scope)_");
        }

        if (options.Detail == DetailLevel.Full && slice.SignatureDepNames.Count > 0)
        {
            sb.AppendLine($"- Signature deps ({slice.SignatureDepsTotal}): {string.Join(", ", slice.SignatureDepNames.Select(n => $"`{n}`"))}"
                          + (slice.SignatureDepsTruncated ? " …" : ""));
        }

        var cons = RelationPresentation.Slice(m.ConsumersJson, options.Detail, options.MaxRelations);
        if (cons.ConsumerNames.Count > 0)
        {
            sb.AppendLine($"- Callers ({cons.ConsumersTotal}): {string.Join(", ", cons.ConsumerNames.Select(n => $"`{n}`"))}"
                          + (cons.ConsumersTruncated ? " …" : ""));
        }

        if (slice.AnyTruncated || cons.AnyTruncated)
            sb.AppendLine("- Truncated: `true`");

        if (options.IncludeSnippet)
            AppendSnippetMarkdown(sb, m.RelativePath, m.StartLine, m.EndLine, options);

        return Finalize(sb, options.MaxChars);
    }

    public static string MemberDetailToJson(MemberDetail m, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var payload = ShapeMember(m, options, includeId: true);
        return CapJson(JsonSerializer.Serialize(payload, JsonOpts), options.MaxChars);
    }

    public static string SnippetOnlyMarkdown(SourceSnippet snippet)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Snippet: {snippet.RelativePath}");
        sb.AppendLine();
        sb.AppendLine($"- Lines: L{snippet.StartLine}-{snippet.EndLine} ({snippet.LineCount} lines)"
                      + (snippet.Truncated ? " truncated" : ""));
        sb.AppendLine();
        sb.AppendLine("```csharp");
        sb.AppendLine(snippet.Text);
        sb.AppendLine("```");
        return Finalize(sb, OutputLimits.DefaultMaxChars);
    }

    public static string SnippetOnlyJson(SourceSnippet snippet) =>
        CapJson(JsonSerializer.Serialize(ShapeSnippet(snippet), JsonOpts), OutputLimits.DefaultMaxChars);

    public static string CallersToMarkdown(string methodName, IReadOnlyList<RelationRef> callers, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var max = options.MaxRelations;
        var list = callers.Take(max).ToList();
        var truncated = callers.Count > max;
        var uniqueCallers = callers.Select(c => c.TargetId).Distinct(StringComparer.Ordinal).Count();
        var sb = new StringBuilder();
        sb.AppendLine($"# References to {methodName}");
        sb.AppendLine();
        sb.AppendLine($"Sites: {callers.Count} | Unique containers: {uniqueCallers}"
                      + (truncated ? $" (showing {list.Count} sites)" : ""));
        sb.AppendLine();
        foreach (var c in list)
        {
            var name = RelationPresentation.ShortName(c);
            var site = c.DisplaySiteLabel;
            if (site is not null)
                sb.AppendLine($"- `{name}` @ `{site}`");
            else
                sb.AppendLine($"- `{name}`");
        }
        if (truncated)
            sb.AppendLine().AppendLine("_truncated: true_");
        return Finalize(sb, options.MaxChars);
    }

    public static string ConsumersToMarkdown(
        string typeName,
        IReadOnlyList<RelationRef> consumers,
        ExportOptions? options = null,
        bool updatedDb = false)
    {
        options ??= new ExportOptions();
        var max = options.MaxRelations;
        var list = consumers.Take(max).ToList();
        var truncated = consumers.Count > max;
        var sb = new StringBuilder();
        sb.AppendLine($"# Consumers of {typeName}");
        sb.AppendLine();
        sb.AppendLine($"Count: {consumers.Count}" + (truncated ? $" (showing {list.Count})" : ""));
        sb.AppendLine();
        foreach (var c in list)
            sb.AppendLine($"- `{RelationPresentation.ShortName(c)}`");
        if (truncated)
            sb.AppendLine().AppendLine("_truncated: true_");
        if (updatedDb)
            sb.AppendLine().AppendLine("_Persisted to types.consumers_json._");
        return Finalize(sb, options.MaxChars);
    }

    private static void AppendTypeDeps(StringBuilder sb, string depsJson, ExportOptions options, string indent)
    {
        var slice = RelationPresentation.Slice(depsJson, options.Detail, options.MaxRelations);
        if (slice.StructuralNames.Count > 0)
        {
            sb.AppendLine($"{indent}- Structural ({slice.StructuralTotal}): {string.Join(", ", slice.StructuralNames.Select(n => $"`{n}`"))}"
                          + (slice.StructuralTruncated ? " …" : ""));
        }

        if (options.Detail == DetailLevel.Full && slice.SignatureDepNames.Count > 0)
        {
            sb.AppendLine($"{indent}- Type deps ({slice.SignatureDepsTotal}): {string.Join(", ", slice.SignatureDepNames.Select(n => $"`{n}`"))}"
                          + (slice.SignatureDepsTruncated ? " …" : ""));
        }
    }

    private static void AppendMemberLine(StringBuilder sb, MemberDetail m, ExportOptions options)
    {
        sb.Append($"- `{ShortSig(m.Signature)}`");
        if (m.StartLine is not null)
            sb.Append($"  L{m.StartLine}-{m.EndLine} ({m.LineCount} lines)");
        if (!string.IsNullOrEmpty(m.Summary) && options.Detail == DetailLevel.Full)
            sb.Append($" — {m.Summary}");
        sb.AppendLine();

        var slice = RelationPresentation.Slice(m.DependenciesJson, options.Detail, options.MaxRelations);
        if (slice.CallNames.Count > 0)
        {
            sb.AppendLine($"  - calls ({slice.CallsTotal}): {string.Join(", ", slice.CallNames.Select(n => $"`{n}`"))}"
                          + (slice.CallsTruncated ? " …" : ""));
        }
    }

    private static object ShapeRelations(string depsJson, string consJson, ExportOptions options)
    {
        var deps = RelationPresentation.Slice(depsJson, options.Detail, options.MaxRelations);
        var cons = RelationPresentation.Slice(consJson, options.Detail, options.MaxRelations);
        return new
        {
            calls = deps.CallNames,
            callsTotal = deps.CallsTotal,
            callsTruncated = deps.CallsTruncated,
            structural = deps.StructuralNames,
            structuralTotal = deps.StructuralTotal,
            signatureDeps = options.Detail == DetailLevel.Full ? deps.SignatureDepNames : null,
            signatureDepsTotal = options.Detail == DetailLevel.Full ? deps.SignatureDepsTotal : (int?)null,
            consumers = cons.ConsumerNames,
            consumersTotal = cons.ConsumersTotal,
            consumersTruncated = cons.ConsumersTruncated,
            truncated = deps.AnyTruncated || cons.AnyTruncated
        };
    }

    private static object ShapeMember(MemberDetail m, ExportOptions options, bool includeId = false)
    {
        var deps = RelationPresentation.Slice(m.DependenciesJson, options.Detail, options.MaxRelations);
        var cons = RelationPresentation.Slice(m.ConsumersJson, options.Detail, options.MaxRelations);
        var snippet = options.IncludeSnippet
            ? TrySnippet(m.RelativePath, m.StartLine, m.EndLine, options)
            : null;
        return new
        {
            id = includeId ? m.Id : null,
            m.Name,
            m.Kind,
            signature = ShortSig(m.Signature),
            accessibility = m.Accessibility,
            returnType = options.Detail == DetailLevel.Full ? m.ReturnType : null,
            summary = m.Summary,
            m.RelativePath,
            parentType = m.ParentTypeFullName,
            lines = new { start = m.StartLine, end = m.EndLine, count = m.LineCount },
            sizeChars = options.Detail == DetailLevel.Full ? m.SizeChars : (int?)null,
            calls = deps.CallNames,
            callsTotal = deps.CallsTotal,
            callsTruncated = deps.CallsTruncated,
            signatureDeps = options.Detail == DetailLevel.Full ? deps.SignatureDepNames : null,
            callers = cons.ConsumerNames,
            callersTotal = cons.ConsumersTotal,
            truncated = deps.AnyTruncated || cons.AnyTruncated,
            snippet = snippet is null ? null : ShapeSnippet(snippet),
            tokenEstimate = m.TokenEstimate
        };
    }

    private static void AppendSnippetMarkdown(
        StringBuilder sb,
        string? relativePath,
        int? startLine,
        int? endLine,
        ExportOptions options)
    {
        try
        {
            var snip = TrySnippet(relativePath, startLine, endLine, options);
            if (snip is null)
            {
                sb.AppendLine();
                sb.AppendLine("## Source");
                sb.AppendLine();
                sb.AppendLine("_Snippet unavailable (missing path/lines or file on disk)._");
                return;
            }

            sb.AppendLine();
            sb.AppendLine("## Source");
            sb.AppendLine();
            sb.AppendLine($"```csharp");
            sb.AppendLine(snip.Text);
            sb.AppendLine("```");
            if (snip.Truncated)
                sb.AppendLine("_snippet truncated: true_");
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine($"_Snippet error: {ex.Message}_");
        }
    }

    private static SourceSnippet? TrySnippet(
        string? relativePath,
        int? startLine,
        int? endLine,
        ExportOptions options)
    {
        var absHint = options.ResolveAbsolutePath?.Invoke(relativePath);
        return SourceSnippetReader.TryRead(relativePath, startLine, endLine, new SourceSnippetOptions
        {
            SolutionPath = options.SolutionPath,
            AbsolutePathHint = absHint,
            ContextLines = options.SnippetContextLines,
            MaxChars = options.SnippetMaxChars
        });
    }

    private static object ShapeSnippet(SourceSnippet s) => new
    {
        s.RelativePath,
        lines = new { start = s.StartLine, end = s.EndLine, count = s.LineCount },
        s.Text,
        s.Truncated,
        s.ContextLines,
        tokenEstimate = s.TokenEstimate
    };

    private static string ShortSig(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";
        // Drop leading accessibility/modifiers noise for compact: keep as-is if short
        if (signature.Length <= 120)
            return signature;
        return signature[..119] + "…";
    }

    private static string Finalize(StringBuilder sb, int maxChars)
    {
        var tokens = TokenEstimator.FromText(sb.ToString());
        sb.AppendLine();
        sb.AppendLine($"_~{tokens} tokens_");
        var text = sb.ToString();
        if (text.Length <= maxChars)
            return text;

        var cut = maxChars - 80;
        if (cut < 100)
            cut = maxChars / 2;
        return text[..cut] + "\n\n_… truncated: true (maxChars=" + maxChars + ")_\n";
    }

    private static string CapJson(string json, int maxChars)
    {
        if (json.Length <= maxChars)
            return json;
        // Return minimal truncated envelope
        return JsonSerializer.Serialize(new
        {
            truncated = true,
            maxChars,
            preview = json[..Math.Min(500, json.Length)]
        }, JsonOpts);
    }
}
