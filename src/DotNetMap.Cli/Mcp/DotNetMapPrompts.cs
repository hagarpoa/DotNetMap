using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DotNetMap.Cli.Mcp;

[McpServerPromptType]
public static class DotNetMapPrompts
{
    [McpServerPrompt(Name = "architecture_review"), Description(
        "Prompt template for reviewing solution architecture using the DotNetMap index.")]
    public static string ArchitectureReview(
        [Description("Optional focus area, e.g. ordering, payments")] string? focus = null)
    {
        var focusLine = string.IsNullOrWhiteSpace(focus)
            ? "the whole solution"
            : $"the area: {focus}";

        return $"""
            You are reviewing .NET solution architecture for {focusLine}.

            Workflow:
            1. get_status — if isStale, stop and ask to reindex (--changed-only) before continuing.
            2. get_overview (detail=compact) for a low-token type map.
            3. search for key domain types/services.
            4. get_type on the most important types; use get_consumers for coupling.
            5. get_method + get_callers only where behavior matters.
            6. get_snippet only for methods you will change.
            7. Summarize: layering, coupling hotspots, missing abstractions, concrete refactors.

            Prefer DotNetMap tools over grepping. Stay within token budget (compact default).
            """;
    }

    [McpServerPrompt(Name = "impact_analysis"), Description(
        "Prompt template for change-impact analysis on a type or method.")]
    public static string ImpactAnalysisPrompt(
        [Description("Type or method name to analyze")] string target)
    {
        return $"""
            Analyze the impact of changing `{target}` in this .NET solution.

            Workflow:
            1. get_status (abort if isStale without reindex)
            2. search(query="{target}")
            3. If type: get_type + get_consumers(name="{target}")
               If method: get_method + get_callers(name="{target}") — note file:line sites
            4. For top dependents, get_type/get_method once more
            5. get_snippet only for symbols you plan to edit
            6. Report: direct dependents, call sites, risk level, suggested tests, safe change order

            Use compact tool results; do not dump entire source files unless necessary.
            """;
    }

    [McpServerPrompt(Name = "refactor_plan"), Description(
        "End-to-end refactor planning workflow using DotNetMap (rename, extract method, split class).")]
    public static string RefactorPlan(
        [Description("What to refactor, e.g. rename CalculateTotal or extract validation from SaveAsync")]
        string goal)
    {
        return $"""
            Plan a safe .NET refactor: {goal}

            Mandatory workflow (DotNetMap tools only until the edit step):
            1. get_status
               - If isStale == true: recommend `dotnetmap index <path> --changed-only` and stop.
            2. search to locate the primary type/method names.
            3. get_type / get_method (detail=compact) for signatures, lines, and outbound calls.
            4. Impact:
               - Method change → get_callers (note each site as file:Lline)
               - Type/interface change → get_consumers
            5. get_snippet on symbols you will edit (contextLines=2 if helpful).
            6. Produce a short plan:
               a) Preconditions (stale? tests?)
               b) Ordered edit steps with exact files/lines from DotNetMap
               c) Symbols to re-check after edit (callers/consumers)
               d) Suggested verification (build/tests)
            7. Do NOT invent call sites — only use tool results.
            8. Prefer compact responses; use detail=full or includeSnippet only when needed.

            After the user applies edits, suggest reindex --changed-only and re-run get_callers/get_consumers.
            """;
    }
}
