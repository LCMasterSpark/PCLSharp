using System.IO;

namespace PCLrmkBYCSharp.Models;

public sealed record DownloadFile(
    IReadOnlyList<string> Sources,
    string LocalPath,
    DownloadFileCheck Check,
    bool SimulateBrowserHeaders = false)
{
    public string LocalName => Path.GetFileName(LocalPath);
}
