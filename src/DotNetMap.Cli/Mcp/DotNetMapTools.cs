using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DotNetMap.Core.Analysis;
using DotNetMap.Core.Export;
using DotNetMap.Core.Source;
using DotNetMap.Core.Store;

namespace DotNetMap.Cli.Mcp;

/// <summary>
/// MCP tools for AI agents. Compact by default (DNM-006); expand with detail=full.
/// </summary>
[McpServerToolType]
public static class DotNetMapTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    internal static string DatabasePath { get; set; } =
        Path.Combine(Directory.GetCurrentDirectory(), ".dotnetmap", "index.db");

    [McpServerTool(Name = "get_status"), Description(
        "Returns DotNetMap index status and staleness vs disk (isStale, staleProjects). " +
        "Call this first; if isStale, reindex with --changed-only before trusting the map. " +
        "Example: get_status()")]
    public static string GetStatus()
    {
        using var store = OpenOrThrow();
        var s = store.GetStatus();
        var stale = IndexStaleness.Check(store);
        return JsonSerializer.Serialize(new
        {
            s.SolutionName,
            s.SolutionPath,
            s.IndexedAtUtc,
            s.IndexMode,
            s.SchemaVersion,
            s.ProjectCount,
            s.TypeCount,
            s.MemberCount,
            s.FileCount,
            s.DatabaseBytes,
            s.TokenEstimateOverview,
            s.IncludePrivate,
            s.IncludeTest,
            indexBody = s.IndexBody,
            bodyFileCount = s.BodyFileCount,
            edgeCount = s.EdgeCount,
            s.DotNetMapVersion,
            databasePath = DatabasePath,
            isStale = stale.IsStale,
            staleProjectCount = stale.StaleProjects.Count,
            staleProjects = stale.StaleProjects,
            missingFileCount = stale.MissingFiles.Count,
            newFileCount = stale.NewFiles.Count,
            staleDetails = stale.Details.Take(15).ToList(),
            quality = IndexQuality.Compute(store)
        }, JsonOpts);
    }

    [McpServerTool(Name = "doctor"), Description(
        "Run environment + index health checks (MSBuild, database, schema, staleness, quality grade). " +
        "Example: doctor()")]
    public static string RunDoctor()
    {
        var report = Doctor.Run(DatabasePath);
        return Doctor.FormatMarkdown(report);
    }

    [McpServerTool(Name = "get_overview"), Description(
        "Compact solution overview (low tokens). detail=compact|full. " +
        "Example: get_overview(maxTypes=50, detail=\"compact\")")]
    public static string GetOverview(
        [Description("Max types (default 80, max 200)")] int maxTypes = 80,
        [Description("compact (default) or full")] string detail = "compact")
    {
        maxTypes = Math.Clamp(maxTypes, 1, 200);
        using var store = OpenOrThrow();
        return CompactExporter.ToMarkdown(store, new ExportOptions
        {
            MaxTypes = maxTypes,
            IncludeMembers = false,
            IncludeDeps = false,
            Detail = ParseDetail(detail),
            MaxChars = OutputLimits.DefaultMaxChars
        });
    }

    [McpServerTool(Name = "search"), Description(
        "FTS5 search over type/member names and summaries. Prefer before grepping. " +
        "Set body=true to search source text (requires index --index-body). Returns file:line for body hits. " +
        "Example: search(query=\"OrderService\", kind=\"type\", max=10); search(query=\"TODO\", body=true)")]
    public static string Search(
        [Description("Free-text query")] string query,
        [Description("all | type | member (ignored when body=true)")] string kind = "all",
        [Description("Max hits (default 15, max 50)")] int max = 15,
        [Description("md or json")] string format = "md",
        [Description("compact or full")] string detail = "compact",
        [Description("Search indexed source bodies (DNM-013); needs index --index-body")] bool body = false)
    {
        max = Math.Clamp(max, 1, OutputLimits.DefaultMaxSearchHits);
        kind = string.IsNullOrWhiteSpace(kind) ? "all" : kind.ToLowerInvariant();
        if (kind is not ("all" or "type" or "member"))
            kind = "all";

        using var store = OpenOrThrow();
        if (body && !store.HasBodyIndex())
        {
            return "Body FTS not indexed. Re-run: dotnetmap index <path> --index-body (or set indexBody:true in .dotnetmap.json).";
        }

        var hits = store.Search(query, kind, max, body: body);
        var opts = new ExportOptions { Detail = ParseDetail(detail), MaxChars = OutputLimits.DefaultMaxChars };
        return format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? CompactExporter.SearchToJson(hits, query, opts)
            : CompactExporter.SearchToMarkdown(hits, query, opts);
    }

    [McpServerTool(Name = "get_type"), Description(
        "Get one type with members, lines, structural deps, and outbound calls (compact by default). " +
        "detail=full adds signature type deps. includeSnippet=true reads source from disk (capped). " +
        "Example: get_type(name=\"OrderService\", detail=\"compact\")")]
    public static string GetType(
        [Description("Type name, Full.Name, or type:Id")] string name,
        [Description("md or json")] string format = "md",
        [Description("Max members (default 80)")] int maxMembers = 80,
        [Description("compact or full")] string detail = "compact",
        [Description("Include source snippet from disk")] bool includeSnippet = false,
        [Description("Context lines around span when includeSnippet")] int contextLines = 0)
    {
        maxMembers = Math.Clamp(maxMembers, 1, 200);
        using var store = OpenOrThrow();
        var typeDetail = store.GetTypeDetail(name, maxMembers);
        if (typeDetail is null)
            return $"Type not found: {name}. Try search(query=\"{name}\").";

        var opts = BuildExportOptions(store, detail, includeSnippet, contextLines, maxMembers);
        return format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? CompactExporter.TypeDetailToJson(typeDetail, opts)
            : CompactExporter.TypeDetailToMarkdown(typeDetail, opts);
    }

    [McpServerTool(Name = "get_method"), Description(
        "Get one method with file, lines, lineCount, and solution-local outbound calls (compact). " +
        "includeSnippet=true reads the method body from disk (not stored in DB; max ~4k chars). " +
        "Example: get_method(name=\"SaveAsync\", includeSnippet=true)")]
    public static string GetMethod(
        [Description("Method name, Type.Method, signature, or method:Id")] string name,
        [Description("md or json")] string format = "md",
        [Description("compact or full")] string detail = "compact",
        [Description("Include source snippet from disk")] bool includeSnippet = false,
        [Description("Context lines around span when includeSnippet")] int contextLines = 0)
    {
        using var store = OpenOrThrow();
        var memberDetail = store.GetMemberDetail(name);
        if (memberDetail is null)
            return $"Member not found: {name}. Try search(query=\"{name}\", kind=\"member\").";

        var opts = BuildExportOptions(store, detail, includeSnippet, contextLines);
        return format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? CompactExporter.MemberDetailToJson(memberDetail, opts)
            : CompactExporter.MemberDetailToMarkdown(memberDetail, opts);
    }

    [McpServerTool(Name = "get_snippet"), Description(
        "Read source snippet for a type or method using indexed file+line spans. " +
        "Does not store code in SQLite; reads from disk under solution root only. " +
        "Example: get_snippet(name=\"OrderService.SaveAsync\", contextLines=2)")]
    public static string GetSnippet(
        [Description("Type or method name")] string name,
        [Description("Extra lines before/after (0-20)")] int contextLines = 0,
        [Description("Max chars (default 4000)")] int maxChars = 4000,
        [Description("md or json")] string format = "md")
    {
        try
        {
            using var store = OpenOrThrow();
            var status = store.GetStatus();
            string? rel = null;
            int? start = null;
            int? end = null;

            var member = store.GetMemberDetail(name);
            if (member is not null)
            {
                rel = member.RelativePath;
                start = member.StartLine;
                end = member.EndLine;
            }
            else
            {
                var type = store.GetTypeDetail(name);
                if (type is null)
                    return $"Not found: {name}. Try search first.";
                rel = type.RelativePath;
                start = type.StartLine;
                end = type.EndLine;
            }

            var snip = SourceSnippetReader.TryRead(rel, start, end, new SourceSnippetOptions
            {
                SolutionPath = status.SolutionPath,
                AbsolutePathHint = store.ResolveFileAbsolutePath(rel),
                ContextLines = contextLines,
                MaxChars = Math.Clamp(maxChars, 200, 50_000)
            });

            if (snip is null)
                return "Snippet unavailable (missing path, lines, or file on disk).";

            return format.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? CompactExporter.SnippetOnlyJson(snip)
                : CompactExporter.SnippetOnlyMarkdown(snip);
        }
        catch (Exception ex)
        {
            return $"get_snippet failed: {ex.Message}";
        }
    }

    private static ExportOptions BuildExportOptions(
        MapStore store,
        string detail,
        bool includeSnippet,
        int contextLines,
        int maxMembers = 80) =>
        new()
        {
            Detail = ParseDetail(detail),
            MaxChars = OutputLimits.DefaultMaxChars,
            MaxRelations = OutputLimits.DefaultMaxRelations,
            MaxMembersPerType = maxMembers,
            IncludeSnippet = includeSnippet,
            SnippetContextLines = Math.Clamp(contextLines, 0, 20),
            SolutionPath = store.GetStatus().SolutionPath,
            ResolveAbsolutePath = store.ResolveFileAbsolutePath
        };

    [McpServerTool(Name = "get_callers"), Description(
        "Find reference sites for a method, property, or field (on-demand SymbolFinder; can be slow). " +
        "Each site includes file:line. Use before rename/signature change. " +
        "Example: get_callers(name=\"LineTotal\") or get_callers(name=\"Order.Lines\", updateDb=true)")]
    public static async Task<string> GetCallers(
        [Description("Method/property/field name, Type.Member, or method:/property:/field:Id")] string name,
        [Description("Max callers (default 50)")] int max = 50,
        [Description("Persist into index consumers_json")] bool updateDb = false,
        [Description("md or json")] string format = "md",
        [Description("compact or full")] string detail = "compact",
        CancellationToken cancellationToken = default)
    {
        max = Math.Clamp(max, 1, 100);
        try
        {
            using var store = OpenOrThrow();
            var result = await ImpactAnalysis.GetCallersAsync(store, name, updateDb, max, cancellationToken)
                .ConfigureAwait(false);
            return ImpactAnalysis.FormatCallers(result, format, ParseDetail(detail));
        }
        catch (Exception ex)
        {
            return $"get_callers failed: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_consumers"), Description(
        "Find types that reference/implement the given type (on-demand SymbolFinder; can be slow). " +
        "Use for impact analysis on classes/interfaces. " +
        "Example: get_consumers(name=\"IOrderService\") or get_consumers(name=\"Demo.Core.Order\", updateDb=true)")]
    public static async Task<string> GetConsumers(
        [Description("Type name, Full.Name, or type:Id")] string name,
        [Description("Max consumers (default 50)")] int max = 50,
        [Description("Persist into index consumers_json")] bool updateDb = false,
        [Description("md or json")] string format = "md",
        [Description("compact or full")] string detail = "compact",
        CancellationToken cancellationToken = default)
    {
        max = Math.Clamp(max, 1, 100);
        try
        {
            using var store = OpenOrThrow();
            var result = await ImpactAnalysis.GetTypeConsumersAsync(store, name, updateDb, max, cancellationToken)
                .ConfigureAwait(false);
            return ImpactAnalysis.FormatConsumers(result, format, ParseDetail(detail));
        }
        catch (Exception ex)
        {
            return $"get_consumers failed: {ex.Message}";
        }
    }

    [McpServerTool(Name = "find_implementations"), Description(
        "Find types that implement an interface or extend a base class (SymbolFinder). " +
        "Prefer this over get_consumers when you only need the hierarchy, not all references. " +
        "Example: find_implementations(name=\"IOrderService\") or find_implementations(name=\"OrderCalculator\")")]
    public static async Task<string> FindImplementations(
        [Description("Interface or base type name")] string name,
        [Description("Max results (default 50)")] int max = 50,
        [Description("md or json")] string format = "md",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var store = OpenOrThrow();
            var result = await HierarchyQueries.FindImplementationsAsync(store, name, max, cancellationToken)
                .ConfigureAwait(false);
            return HierarchyQueries.FormatImplementations(result, format);
        }
        catch (Exception ex)
        {
            return $"find_implementations failed: {ex.Message}";
        }
    }

    [McpServerTool(Name = "find_overrides"), Description(
        "Find methods that override a virtual/abstract method, or implement an interface method. " +
        "Example: find_overrides(name=\"OrderCalculator.Adjust\") or find_overrides(name=\"Adjust\")")]
    public static async Task<string> FindOverrides(
        [Description("Method name or Type.Method")] string name,
        [Description("Max results (default 50)")] int max = 50,
        [Description("md or json")] string format = "md",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var store = OpenOrThrow();
            var result = await HierarchyQueries.FindOverridesAsync(store, name, max, cancellationToken)
                .ConfigureAwait(false);
            return HierarchyQueries.FormatOverrides(result, format);
        }
        catch (Exception ex)
        {
            return $"find_overrides failed: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_hotspots"), Description(
        "List index hotspots to prioritize refactors. Metrics: size (largest methods), calls (most outbound calls), " +
        "fanin (most stored reference sites — run get_callers with updateDb first), types (most members). " +
        "Example: get_hotspots(by=\"size\", top=10) or get_hotspots(by=\"calls\")")]
    public static string GetHotspots(
        [Description("size | calls | fanin | types")] string by = "size",
        [Description("Top N (default 15, max 50)")] int top = 15,
        [Description("md or json")] string format = "md")
    {
        try
        {
            using var store = OpenOrThrow();
            var metric = Hotspots.ParseMetric(by);
            var result = Hotspots.Compute(store, metric, top);
            return Hotspots.Format(result, format);
        }
        catch (Exception ex)
        {
            return $"get_hotspots failed: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_impact"), Description(
        "Build a compact multi-hop impact graph around a type or member. " +
        "Hop 0 can use live callers/consumers/implementations; deeper hops use index edges only. " +
        "Example: get_impact(name=\"IOrderService\", depth=2, direction=\"both\") or get_impact(name=\"SaveAsync\", direction=\"in\")")]
    public static async Task<string> GetImpact(
        [Description("Type or member name")] string name,
        [Description("Max hops 1-4 (default 2)")] int depth = 2,
        [Description("Max nodes (default 40)")] int maxNodes = 40,
        [Description("both | in | out")] string direction = "both",
        [Description("Skip live SymbolFinder on hop 0")] bool noLive = false,
        [Description("md or json")] string format = "md",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var store = OpenOrThrow();
            var dir = direction.Trim().ToLowerInvariant() switch
            {
                "in" or "inbound" => ImpactGraph.Direction.Inbound,
                "out" or "outbound" => ImpactGraph.Direction.Outbound,
                _ => ImpactGraph.Direction.Both
            };
            var result = await ImpactGraph.BuildAsync(
                store, name, depth, maxNodes, dir, liveHop0: !noLive, cancellationToken)
                .ConfigureAwait(false);
            return ImpactGraph.Format(result, format);
        }
        catch (Exception ex)
        {
            return $"get_impact failed: {ex.Message}";
        }
    }

    private static DetailLevel ParseDetail(string? value) =>
        (value ?? "compact").Trim().ToLowerInvariant() switch
        {
            "full" => DetailLevel.Full,
            _ => DetailLevel.Compact
        };

    private static MapStore OpenOrThrow()
    {
        if (!File.Exists(DatabasePath))
            throw new InvalidOperationException(
                $"DotNetMap database not found at '{DatabasePath}'. Run: dotnetmap index <solution> --db {DatabasePath}");

        var store = MapStore.Open(DatabasePath);
        if (!store.HasSolutionData())
        {
            store.Dispose();
            throw new InvalidOperationException(
                $"DotNetMap database at '{DatabasePath}' is empty. Run: dotnetmap index <solution>");
        }

        return store;
    }
}
