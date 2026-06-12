using System.Net;
using System.Text.RegularExpressions;

namespace PCLrmkBYCSharp.Services;

public static class HelpTextExtractor
{
    private static readonly Regex CommentRegex = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        """\b(Title|Text|Content|Info|ToolTip)\s*=\s*(["'])(.*?)\2""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Extract(string xamlContent, int maxLines = 80)
    {
        if (string.IsNullOrWhiteSpace(xamlContent))
        {
            return "";
        }

        var clean = CommentRegex.Replace(xamlContent, "");
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in AttributeRegex.Matches(clean))
        {
            var value = Normalize(match.Groups[3].Value);
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                continue;
            }

            lines.Add(value);
            if (lines.Count >= maxLines)
            {
                break;
            }
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string Normalize(string value)
    {
        var decoded = WebUtility.HtmlDecode(value)
            .Replace("&#xa;", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("&#x0a;", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = decoded.Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
        return string.Join(Environment.NewLine, lines);
    }
}
