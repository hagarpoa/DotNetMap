using DotNetMap.Core.Domain;

namespace DotNetMap.Core.Source;

public sealed record SourceSnippet(
    string AbsolutePath,
    string RelativePath,
    int StartLine,
    int EndLine,
    string Text,
    bool Truncated,
    int ContextLines)
{
    public int TokenEstimate => TokenEstimator.FromText(Text);
    public int LineCount => EndLine >= StartLine ? EndLine - StartLine + 1 : 0;
}

public sealed class SourceSnippetOptions
{
    /// <summary>Extra lines before/after the span (default 0).</summary>
    public int ContextLines { get; init; }

    /// <summary>Hard cap on returned text (default 4000).</summary>
    public int MaxChars { get; init; } = 4_000;

    /// <summary>Solution root or .sln/.slnx path for path allowlist.</summary>
    public string? SolutionPath { get; init; }

    /// <summary>Preferred absolute path from DB.</summary>
    public string? AbsolutePathHint { get; init; }
}
