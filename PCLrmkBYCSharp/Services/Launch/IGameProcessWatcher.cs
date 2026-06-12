using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IGameProcessWatcher
{
    Task WatchAsync(Process process, CancellationToken cancellationToken = default);
}
