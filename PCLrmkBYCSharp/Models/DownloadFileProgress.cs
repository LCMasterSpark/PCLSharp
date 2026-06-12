namespace PCLrmkBYCSharp.Models;

public sealed record DownloadFileProgress(
    string LocalPath,
    DownloadFileState State,
    long BytesReceived,
    long TotalBytes,
    string Message);
