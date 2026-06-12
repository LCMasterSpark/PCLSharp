namespace PCLrmkBYCSharp.Models;

public sealed record DownloadFileCheck(
    long MinSize = -1,
    long ActualSize = -1,
    string? Hash = null,
    bool CanUseExistingFile = true,
    bool IsJson = false);
