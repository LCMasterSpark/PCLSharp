using System.IO;

namespace PCLrmkBYCSharp.Models;

public sealed record LocalModFile(
    string FilePath,
    string FileName,
    string EnabledFileName,
    bool IsEnabled,
    string DisplayName,
    string Version,
    string Description,
    long SizeBytes,
    DateTimeOffset LastWriteTime)
{
    public string BaseName => Path.GetFileNameWithoutExtension(EnabledFileName);

    public string StateText => IsEnabled ? "已启用" : "已禁用";

    public string SizeText => SizeBytes switch
    {
        < 1024 => SizeBytes + " B",
        < 1024 * 1024 => (SizeBytes / 1024D).ToString("0.#") + " KB",
        < 1024L * 1024 * 1024 => (SizeBytes / 1024D / 1024D).ToString("0.#") + " MB",
        _ => (SizeBytes / 1024D / 1024D / 1024D).ToString("0.##") + " GB"
    };
}
