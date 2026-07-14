using System.ComponentModel;
using ModelContextProtocol.Server;
using NetMap.Core.Export;
using NetMap.Core.Store;

namespace NetMap.Cli.Mcp;

[McpServerResourceType]
public static class NetMapResources
{
    [McpServerResource(UriTemplate = "solution://overview", Name = "SolutionOverview", MimeType = "text/markdown")]
    [Description("Compact Markdown overview of the indexed solution.")]
    public static string SolutionOverview()
    {
        using var store = MapStore.Open(NetMapTools.DatabasePath);
        if (!store.HasSolutionData())
            return "Index is empty.";
        return CompactExporter.ToMarkdown(store, new ExportOptions { MaxTypes = 100 });
    }

    [McpServerResource(UriTemplate = "type://{name}", Name = "TypeDetail", MimeType = "text/markdown")]
    [Description("Markdown detail for a type by name or full name.")]
    public static string TypeDetail(string name)
    {
        using var store = MapStore.Open(NetMapTools.DatabasePath);
        if (!store.HasSolutionData())
            return "Index is empty.";
        var detail = store.GetTypeDetail(name);
        return detail is null
            ? $"Type not found: {name}"
            : CompactExporter.TypeDetailToMarkdown(detail);
    }
}
