namespace PCLrmkBYCSharp.Models;

public sealed record LoaderVersionOption(
    string LoaderKind,
    string Version,
    bool IsStable,
    string SourceName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(SourceName)
        ? Version
        : $"{Version} / {SourceName}";

    public override string ToString() => DisplayName;
}
