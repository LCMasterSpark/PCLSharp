namespace PCLrmkBYCSharp.Models;

public sealed record ModpackInstallPlan(
    string Name,
    string InstanceName,
    string MinecraftVersion,
    string? LoaderName,
    string? LoaderVersion,
    string InstancePath,
    IReadOnlyList<DownloadFile> Files,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LoaderProcessorStep> ProcessorSteps,
    int OverrideFileCount);
