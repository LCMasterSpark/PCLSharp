using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface IDownloadManagerService
{
    event EventHandler<DownloadTaskSnapshot>? SnapshotChanged;

    IReadOnlyList<DownloadTaskSnapshot> Tasks { get; }

    Task<DownloadTaskSnapshot> DownloadAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken = default);

    bool Cancel(string name);

    int CancelAllRunning();

    Task<DownloadTaskSnapshot?> RetryAsync(string name, CancellationToken cancellationToken = default);

    int ClearFinished();
}
