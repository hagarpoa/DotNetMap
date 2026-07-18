using System.ComponentModel;
using ModelContextProtocol.Server;
using DotNetMap.Core.Export;
using DotNetMap.Core.Store;

namespace DotNetMap.Cli.Mcp;

[McpServerResourceType]
public static class DotNetMapResources
{
    [McpServerResource(UriTemplate = "solution://overview", Name = "SolutionOverview", MimeType = "text/markdown")]
    [Description("Compact Markdown overview of the indexed solution.")]
    public static string SolutionOverview()
    {
        using var store = MapStore.Open(DotNetMapTools.DatabasePath);
        if (!store.HasSolutionData())
            return "Index is empty.";
        return CompactExporter.ToMarkdown(store, new ExportOptions { MaxTypes = 100 });
    }

    [McpServerResource(UriTemplate = "type://{name}", Name = "TypeDetail", MimeType = "text/markdown")]
    [Description("Markdown detail for a type by name or full name.")]
    public static string TypeDetail(string name)
    {
        using var store = MapStore.Open(DotNetMapTools.DatabasePath);
        if (!store.HasSolutionData())
            return "Index is empty.";
        var detail = store.GetTypeDetail(name);
        return detail is null
            ? $"Type not found: {name}"
            : CompactExporter.TypeDetailToMarkdown(detail);
    }

    [McpServerResource(UriTemplate = "method://{name}", Name = "MethodDetail", MimeType = "text/markdown")]
    [Description("Markdown detail for a method/member by name (same content as get_method tool). DNM-039.")]
    public static string MethodDetail(string name)
    {
        using var store = MapStore.Open(DotNetMapTools.DatabasePath);
        if (!store.HasSolutionData())
            return "Index is empty.";
        var detail = store.GetMemberDetail(name);
        return detail is null
            ? $"Member not found: {name}"
            : CompactExporter.MemberDetailToMarkdown(detail);
    }
}
