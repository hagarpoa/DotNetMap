using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace NetMap.Core.Extraction;

public static partial class XmlSummary
{
    public static string? FromSymbol(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: default);
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var summary = doc.Descendants("summary").FirstOrDefault();
            if (summary is null)
                return null;

            var text = string.Concat(summary.Nodes().Select(n => n.ToString()));
            text = CollapseWhitespace(StripXmlTags(text));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            // Fallback: crude extraction if XML is malformed
            var m = SummaryRegex().Match(xml);
            if (!m.Success)
                return null;
            var text = CollapseWhitespace(StripXmlTags(m.Groups[1].Value));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }

    private static string StripXmlTags(string value) =>
        TagRegex().Replace(value, "");

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    [GeneratedRegex(@"<summary>(.*?)</summary>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SummaryRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
