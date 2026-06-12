using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public static class HelpDocumentParser
{
    private static readonly Regex CommentRegex = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(
        @"<(?<tag>[\w:]+)\b(?<attrs>[^>]*)/?>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        """(?<name>[\w:.\-]+)\s*=\s*(["'])(?<value>.*?)\2""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static IReadOnlyList<HelpDocumentBlock> Parse(HelpEntry? entry)
    {
        if (entry is null)
        {
            return [];
        }

        if (entry.IsEvent)
        {
            return
            [
                new HelpDocumentBlock("Event", entry.Title, entry.Description, entry.EventType, entry.EventData)
            ];
        }

        if (string.IsNullOrWhiteSpace(entry.XamlContent))
        {
            return [];
        }

        var blocks = new List<HelpDocumentBlock>();
        var clean = CommentRegex.Replace(entry.XamlContent, "");
        if (TryParseXml(clean, blocks))
        {
            return blocks.Count == 0
                ? [new HelpDocumentBlock("Paragraph", "", HelpTextExtractor.Extract(entry.XamlContent))]
                : blocks;
        }

        foreach (Match match in TagRegex.Matches(clean))
        {
            var tag = match.Groups["tag"].Value;
            var attrs = ReadAttributes(match.Groups["attrs"].Value);
            if (tag.EndsWith("MyCard", StringComparison.OrdinalIgnoreCase))
            {
                AddIfNotBlank(blocks, "CardTitle", Get(attrs, "Title"), "");
            }
            else if (tag.EndsWith("TextBlock", StringComparison.OrdinalIgnoreCase))
            {
                AddIfNotBlank(blocks, "Paragraph", "", Get(attrs, "Text"));
            }
            else if (tag.EndsWith("MyHint", StringComparison.OrdinalIgnoreCase))
            {
                var isWarn = Get(attrs, "IsWarn");
                AddIfNotBlank(
                    blocks,
                    string.Equals(isWarn, "True", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Hint",
                    "",
                    Get(attrs, "Text"));
            }
            else if (tag.EndsWith("MyListItem", StringComparison.OrdinalIgnoreCase))
            {
                var title = Get(attrs, "Title");
                var info = Get(attrs, "Info");
                AddIfNotBlank(blocks, "ListItem", title, info, GetEventType(attrs), GetEventData(attrs));
            }
            else if (tag.EndsWith("MyButton", StringComparison.OrdinalIgnoreCase)
                     || tag.EndsWith("MyTextButton", StringComparison.OrdinalIgnoreCase))
            {
                var text = FirstNonEmpty(Get(attrs, "Text"), Get(attrs, "Content"));
                AddIfNotBlank(blocks, "Action", text, "", GetEventType(attrs), GetEventData(attrs));
            }
            else if (tag.EndsWith("MyImage", StringComparison.OrdinalIgnoreCase)
                     || tag.EndsWith("Image", StringComparison.OrdinalIgnoreCase))
            {
                AddIfNotBlank(blocks, "Image", "", Get(attrs, "Source"));
            }
        }

        return blocks.Count == 0
            ? [new HelpDocumentBlock("Paragraph", "", HelpTextExtractor.Extract(entry.XamlContent))]
            : blocks;
    }

    private static bool TryParseXml(string xamlContent, List<HelpDocumentBlock> blocks)
    {
        try
        {
            var document = XDocument.Parse(
                "<Root xmlns:local=\"urn:pcl-local\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"
                + xamlContent
                + "</Root>",
                LoadOptions.PreserveWhitespace);
            foreach (var element in document.Root?.Elements() ?? [])
            {
                AddElement(element, blocks);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddElement(XElement element, List<HelpDocumentBlock> blocks)
    {
        var tag = element.Name.LocalName;
        var attrs = element.Attributes()
            .ToDictionary(
                attribute => attribute.Name.LocalName,
                attribute => Normalize(attribute.Value),
                StringComparer.OrdinalIgnoreCase);
        if (tag.Equals("MyCard", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotBlank(blocks, "CardTitle", Get(attrs, "Title"), "");
        }
        else if (tag.Equals("TextBlock", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotBlank(blocks, "Paragraph", "", Get(attrs, "Text"));
        }
        else if (tag.Equals("MyHint", StringComparison.OrdinalIgnoreCase))
        {
            var isWarn = Get(attrs, "IsWarn");
            AddIfNotBlank(
                blocks,
                string.Equals(isWarn, "True", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Hint",
                "",
                Get(attrs, "Text"));
        }
        else if (tag.Equals("MyListItem", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotBlank(
                blocks,
                "ListItem",
                Get(attrs, "Title"),
                Get(attrs, "Info"),
                GetEventType(attrs),
                GetEventData(attrs),
                GetNestedEvents(element));
        }
        else if (tag.Equals("MyButton", StringComparison.OrdinalIgnoreCase)
                 || tag.Equals("MyTextButton", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotBlank(
                blocks,
                "Action",
                FirstNonEmpty(Get(attrs, "Text"), Get(attrs, "Content")),
                "",
                GetEventType(attrs),
                GetEventData(attrs),
                GetNestedEvents(element));
        }
        else if (tag.Equals("MyImage", StringComparison.OrdinalIgnoreCase)
                 || tag.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotBlank(blocks, "Image", "", Get(attrs, "Source"));
        }

        foreach (var child in element.Elements())
        {
            if (!IsCustomEventElement(child))
            {
                AddElement(child, blocks);
            }
        }
    }

    private static Dictionary<string, string> ReadAttributes(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(value))
        {
            result[match.Groups["name"].Value] = Normalize(match.Groups["value"].Value);
        }

        return result;
    }

    private static string Get(IReadOnlyDictionary<string, string> attrs, string name)
    {
        return attrs.TryGetValue(name, out var value) ? value : "";
    }

    private static string GetEventType(IReadOnlyDictionary<string, string> attrs)
    {
        return FirstNonEmpty(
            Get(attrs, "EventType"),
            Get(attrs, "CustomEventService.EventType"),
            Get(attrs, "local:CustomEventService.EventType"));
    }

    private static string GetEventData(IReadOnlyDictionary<string, string> attrs)
    {
        return FirstNonEmpty(
            Get(attrs, "EventData"),
            Get(attrs, "CustomEventService.EventData"),
            Get(attrs, "local:CustomEventService.EventData"));
    }

    private static IReadOnlyList<HelpDocumentEvent> GetNestedEvents(XElement element)
    {
        var events = element
            .Descendants()
            .Where(child => child.Name.LocalName.Equals("CustomEvent", StringComparison.OrdinalIgnoreCase))
            .Select(child =>
            {
                var attrs = child.Attributes()
                    .ToDictionary(
                        attribute => attribute.Name.LocalName,
                        attribute => Normalize(attribute.Value),
                        StringComparer.OrdinalIgnoreCase);
                return new HelpDocumentEvent(Get(attrs, "Type"), Get(attrs, "Data"));
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.EventType))
            .ToArray();
        return events;
    }

    private static bool IsCustomEventElement(XElement element)
    {
        return element.Name.LocalName.Equals("CustomEventService.Events", StringComparison.OrdinalIgnoreCase)
            || element.Name.LocalName.Equals("CustomEventCollection", StringComparison.OrdinalIgnoreCase)
            || element.Name.LocalName.Equals("CustomEvent", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIfNotBlank(
        ICollection<HelpDocumentBlock> blocks,
        string kind,
        string title,
        string text,
        string eventType = "",
        string eventData = "",
        IReadOnlyList<HelpDocumentEvent>? events = null)
    {
        var eventCollection = events ?? [];
        if (string.IsNullOrWhiteSpace(title)
            && string.IsNullOrWhiteSpace(text)
            && string.IsNullOrWhiteSpace(eventType)
            && eventCollection.Count == 0)
        {
            return;
        }

        blocks.Add(new HelpDocumentBlock(kind, title, text, eventType, eventData, eventCollection));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
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
