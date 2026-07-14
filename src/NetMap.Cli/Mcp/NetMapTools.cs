using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NetMap.Core.Export;
using NetMap.Core.Store;

namespace NetMap.Cli.Mcp;

/// <summary>
/// MCP tools for AI agents. Compact by default; expand with flags.
/// </summary>
[McpServerToolType]
public static class NetMapTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    internal static string DatabasePath { get; set; } =
        Path.Combine(Directory.GetCurrentDirectory(), ".netmap", "index.db");

    [McpServerTool(Name = "get_status"), Description(
        "Returns NetMap index status: solution path, last indexed time, mode, project/type/member counts, DB size, token estimate. " +
        "Call this first to verify the index is ready. Example: get_status()")]
    public static string GetStatus()
    {
        using var store = OpenOrThrow();
        var s = store.GetStatus();
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
            s.NetMapVersion,
            databasePath = DatabasePath
        }, JsonOpts);
    }

    [McpServerTool(Name = "get_overview"), Description(
        "Compact solution overview: list of types with kind, accessibility, member counts, and short summaries. " +
        "Optimized for AI context (low tokens). Use get_type for details. " +
        "Example: get_overview(maxTypes=50)")]
    public static string GetOverview(
        [Description("Max types to include (default 80, max 200)")] int maxTypes = 80)
    {
        maxTypes = Math.Clamp(maxTypes, 1, 200);
        using var store = OpenOrThrow();
        return CompactExporter.ToMarkdown(store, new ExportOptions
        {
            MaxTypes = maxTypes,
            IncludeMembers = false,
            IncludeDeps = false
        });
    }

    [McpServerTool(Name = "search"), Description(
        "Full-text search over type and member names/summaries (FTS5). " +
        "Returns compact hits. Prefer this before grepping the codebase. " +
        "Example: search(query=\"OrderService\", kind=\"type\", max=10)")]
    public static string Search(
        [Description("Free-text query, e.g. OrderService or CalculateTotal")] string query,
        [Description("all | type | member (default all)")] string kind = "all",
        [Description("Max hits (default 15, max 50)")] int max = 15,
        [Description("md or json (default md)")] string format = "md")
    {
        max = Math.Clamp(max, 1, 50);
        kind = string.IsNullOrWhiteSpace(kind) ? "all" : kind.ToLowerInvariant();
        if (kind is not ("all" or "type" or "member"))
            kind = "all";

        using var store = OpenOrThrow();
        var hits = store.Search(query, kind, max);
        return format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? CompactExporter.SearchToJson(hits, query)
            : CompactExporter.SearchToMarkdown(hits, query);
    }

    [McpServerTool(Name = "get_type"), Description(
        "Get one type with members, source location, summary, and light dependencies/consumers. " +
        "Accepts short name, full name, or type:Id. Compact Markdown by default. " +
        "Example: get_type(name=\"OrderService\") or get_type(name=\"Demo.Core.Order\", format=\"json\")")]
    public static string GetType(
        [Description("Type name, Full.Name, or type:Id")] string name,
        [Description("md or json (default md)")] string format = "md",
        [Description("Max members (default 80, max 200)")] int maxMembers = 80)
    {
        maxMembers = Math.Clamp(maxMembers, 1, 200);
        using var store = OpenOrThrow();
        var detail = store.GetTypeDetail(name, maxMembers);
        if (detail is null)
            return $"Type not found: {name}. Try search(query=\"{name}\").";

        // Truncate huge JSON payloads for token safety
        if (detail.ConsumersJson.Length > 4000 || detail.DependenciesJson.Length > 4000)
        {
            // return as-is; CompactExporter already truncates in md
        }

        return format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? CompactExporter.TypeDetailToJson(detail)
            : CompactExporter.TypeDetailToMarkdown(detail);
    }

    private static MapStore OpenOrThrow()
    {
        if (!File.Exists(DatabasePath))
            throw new InvalidOperationException(
                $"NetMap database not found at '{DatabasePath}'. Run: netmap index <solution> --db {DatabasePath}");

        var store = MapStore.Open(DatabasePath);
        if (!store.HasSolutionData())
        {
            store.Dispose();
            throw new InvalidOperationException(
                $"NetMap database at '{DatabasePath}' is empty. Run: netmap index <solution>");
        }

        return store;
    }
}
