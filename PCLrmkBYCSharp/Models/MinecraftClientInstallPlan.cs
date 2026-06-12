namespace PCLrmkBYCSharp.Models;

public sealed record MinecraftClientInstallPlan(
    string VersionId,
    string InstanceName,
    string VersionFolder,
    IReadOnlyList<DownloadFile> Files);
