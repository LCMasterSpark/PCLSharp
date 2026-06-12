namespace PCLrmkBYCSharp.Models;

public sealed record LoaderInstallPlan(
    string LoaderName,
    string LoaderVersion,
    string VersionJsonPath,
    IReadOnlyList<DownloadFile> Files,
    IReadOnlyList<LoaderProcessorStep> Processors);
