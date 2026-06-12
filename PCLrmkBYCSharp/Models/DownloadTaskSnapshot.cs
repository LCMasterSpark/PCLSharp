namespace PCLrmkBYCSharp.Models;

public sealed record DownloadTaskSnapshot(
    string Name,
    DownloadTaskState State,
    int TotalFiles,
    int FinishedFiles,
    long BytesReceived,
    double Progress,
    string Message)
{
    public bool CanCancel { get; init; }

    public bool CanRetry { get; init; }

    public string PrimaryLocalPath { get; init; } = "";

    public IReadOnlyList<string> LocalPaths { get; init; } = [];

    public string StateText => State switch
    {
        DownloadTaskState.Waiting => "等待中",
        DownloadTaskState.Running => "下载中",
        DownloadTaskState.Succeeded => "已完成",
        DownloadTaskState.Failed => "失败",
        DownloadTaskState.Canceled => "已取消",
        _ => State.ToString()
    };
}
