using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IGameProcessWatcher
{
    Task<GameProcessWatchResult> WatchAsync(Process process, CancellationToken cancellationToken = default);
}
