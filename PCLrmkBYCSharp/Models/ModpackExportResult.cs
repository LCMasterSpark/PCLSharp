namespace PCLrmkBYCSharp.Models;

public sealed record ModpackExportResult(
    string TargetPath,
    int OverrideFileCount,
    IReadOnlyList<string> Warnings);
