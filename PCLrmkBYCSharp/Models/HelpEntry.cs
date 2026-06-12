namespace PCLrmkBYCSharp.Models;

public sealed record HelpEntry(
    string Title,
    string Description,
    string Keywords,
    IReadOnlyList<string> Types,
    string RawPath,
    bool IsEvent,
    string EventType,
    string EventData,
    string XamlContent,
    bool ShowInSearch,
    bool ShowInPublic,
    bool ShowInSnapshot)
{
    public string TypeText => Types.Count == 0 ? "未分类" : string.Join(" / ", Types);
}
