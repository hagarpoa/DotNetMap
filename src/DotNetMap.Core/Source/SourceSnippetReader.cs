namespace DotNetMap.Core.Source;

/// <summary>
/// Reads source snippets on demand from disk (never stored in SQLite).
/// Paths must stay under the solution root (basic allowlist).
/// </summary>
public static class SourceSnippetReader
{
    public static SourceSnippet? TryRead(
        string? relativePath,
        int? startLine,
        int? endLine,
        SourceSnippetOptions? options = null)
    {
        options ??= new SourceSnippetOptions();
        if (string.IsNullOrWhiteSpace(relativePath) && string.IsNullOrWhiteSpace(options.AbsolutePathHint))
            return null;
        if (startLine is null or < 1 || endLine is null or < 1)
            return null;

        var solutionRoot = ResolveSolutionRoot(options.SolutionPath);
        var absolute = ResolveAbsolutePath(relativePath, options.AbsolutePathHint, solutionRoot);
        if (absolute is null || !File.Exists(absolute))
            return null;

        if (solutionRoot is not null && !IsUnderRoot(absolute, solutionRoot))
            throw new InvalidOperationException(
                $"Refusing to read path outside solution root: {absolute}");

        var context = Math.Clamp(options.ContextLines, 0, 20);
        var maxChars = Math.Clamp(options.MaxChars, 200, 50_000);

        var allLines = File.ReadAllLines(absolute);
        if (allLines.Length == 0)
            return null;

        var from = Math.Max(1, startLine.Value - context);
        var to = Math.Min(allLines.Length, endLine.Value + context);
        if (from > to)
            return null;

        var slice = new List<string>();
        for (var i = from; i <= to; i++)
            slice.Add($"{i,4}| {allLines[i - 1]}");

        var text = string.Join(Environment.NewLine, slice);
        var truncated = false;
        if (text.Length > maxChars)
        {
            truncated = true;
            text = text[..maxChars] + Environment.NewLine + "… (truncated)";
        }

        var rel = relativePath?.Replace('\\', '/')
                  ?? Path.GetFileName(absolute);

        return new SourceSnippet(
            absolute,
            rel,
            from,
            to,
            text,
            truncated,
            context);
    }

    public static string? ResolveSolutionRoot(string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
            return null;

        var full = Path.GetFullPath(solutionPath);
        if (Directory.Exists(full))
            return full;
        if (File.Exists(full))
            return Path.GetDirectoryName(full);
        return null;
    }

    public static string? ResolveAbsolutePath(
        string? relativePath,
        string? absoluteHint,
        string? solutionRoot)
    {
        if (!string.IsNullOrWhiteSpace(absoluteHint))
        {
            var full = Path.GetFullPath(absoluteHint);
            if (File.Exists(full))
                return full;
        }

        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(solutionRoot))
            return null;

        var combined = Path.GetFullPath(Path.Combine(solutionRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(combined) ? combined : null;
    }

    public static bool IsUnderRoot(string absolutePath, string root)
    {
        var fullFile = Path.GetFullPath(absolutePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   Path.GetFullPath(absolutePath),
                   Path.GetFullPath(root),
                   StringComparison.OrdinalIgnoreCase)
               || Path.GetFullPath(absolutePath)
                   .StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
    }
}
