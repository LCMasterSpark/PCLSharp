namespace PCLrmkBYCSharp.Models;

public sealed record HelpDocumentBlock(
    string Kind,
    string Title,
    string Text,
    string EventType = "",
    string EventData = "",
    IReadOnlyList<HelpDocumentEvent>? EventCollection = null)
{
    public IReadOnlyList<HelpDocumentEvent> Events { get; } = EventCollection ?? [];

    public bool HasAction => !string.IsNullOrWhiteSpace(EventType) || Events.Count > 0;
}
