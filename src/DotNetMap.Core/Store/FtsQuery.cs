using System.Text;
using System.Text.RegularExpressions;

namespace DotNetMap.Core.Store;

/// <summary>
/// Builds safe FTS5 MATCH expressions from free-text user input.
/// </summary>
public static partial class FtsQuery
{
    /// <summary>
    /// Converts "order service" into a prefix AND query: "order*" AND "service*"
    /// Strips FTS special characters that would break MATCH.
    /// </summary>
    public static string ToMatchExpression(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return "\"\"";

        var tokens = TokenRegex().Matches(userText)
            .Select(m => m.Value)
            .Where(t => t.Length > 0)
            .Take(12)
            .ToList();

        if (tokens.Count == 0)
            return "\"\"";

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
                sb.Append(" AND ");

            var token = tokens[i].Replace("\"", "");
            // Prefix match helps with partial type names
            sb.Append('"').Append(token).Append('"').Append('*');
        }

        return sb.ToString();
    }

    /// <summary>Simple LIKE pattern for fallback / exact-ish contains.</summary>
    public static string ToLikePattern(string userText)
    {
        var cleaned = LikeUnsafe().Replace(userText.Trim(), "");
        if (string.IsNullOrEmpty(cleaned))
            return "%";
        return "%" + cleaned.Replace("%", "").Replace("_", "") + "%";
    }

    /// <summary>Tokenize free text the same way as FTS MATCH (alphanumeric + _ .).</summary>
    public static IReadOnlyList<string> ExtractTokens(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return [];

        return TokenRegex().Matches(userText)
            .Select(m => m.Value)
            .Where(t => t.Length > 0)
            .Take(12)
            .ToList();
    }

    /// <summary>
    /// Locate the first 1-based line containing any of <paramref name="tokens"/> (case-insensitive).
    /// Returns a trimmed single-line snippet (capped).
    /// </summary>
    public static (int? Line, string? Snippet) FindFirstMatchLine(string content, IReadOnlyList<string> tokens)
    {
        if (string.IsNullOrEmpty(content))
            return (null, null);

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (tokens.Count == 0)
        {
            var first = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return (first is null ? null : 1, CapSnippet(first));
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var t in tokens)
            {
                if (line.Contains(t, StringComparison.OrdinalIgnoreCase))
                    return (i + 1, CapSnippet(line.Trim()));
            }
        }

        // File matched FTS but token scan failed (stemming) — return first non-empty
        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                return (i + 1, CapSnippet(lines[i].Trim()));
        }

        return (1, null);
    }

    private static string? CapSnippet(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return line;
        const int max = 160;
        return line.Length <= max ? line : line[..max] + "…";
    }

    [GeneratedRegex(@"[\p{L}\p{N}_.]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[%_]")]
    private static partial Regex LikeUnsafe();
}
