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

    [GeneratedRegex(@"[\p{L}\p{N}_.]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[%_]")]
    private static partial Regex LikeUnsafe();
}
