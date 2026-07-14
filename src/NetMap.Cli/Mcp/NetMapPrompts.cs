using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NetMap.Cli.Mcp;

[McpServerPromptType]
public static class NetMapPrompts
{
    [McpServerPrompt(Name = "architecture_review"), Description(
        "Prompt template for reviewing solution architecture using the NetMap index.")]
    public static string ArchitectureReview(
        [Description("Optional focus area, e.g. ordering, payments")] string? focus = null)
    {
        var focusLine = string.IsNullOrWhiteSpace(focus)
            ? "the whole solution"
            : $"the area: {focus}";

        return $"""
            You are reviewing .NET solution architecture for {focusLine}.

            Workflow:
            1. Call get_status to confirm the NetMap index is current.
            2. Call get_overview for a compact type map.
            3. Use search to find key domain types and services.
            4. Call get_type on the most important types (include deps/consumers).
            5. Summarize: layering, coupling hotspots, missing abstractions, and concrete refactor suggestions.

            Prefer NetMap tools over reading many source files. Stay within token budget; request detail only when needed.
            """;
    }

    [McpServerPrompt(Name = "impact_analysis"), Description(
        "Prompt template for change-impact analysis on a type or feature.")]
    public static string ImpactAnalysis(
        [Description("Type or feature name to analyze")] string target)
    {
        return $"""
            Analyze the impact of changing `{target}` in this .NET solution.

            Workflow:
            1. get_status
            2. search(query="{target}")
            3. get_type(name="{target}") — note dependencies and consumers
            4. For each important consumer, get_type once more
            5. Report: direct dependents, risk level, suggested test targets, and safe change strategy

            Use compact tool results; do not dump entire source files unless necessary.
            """;
    }
}
