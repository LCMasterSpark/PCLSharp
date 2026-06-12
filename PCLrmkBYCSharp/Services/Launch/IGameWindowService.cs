using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IGameWindowService
{
    void ScheduleMaximize(Process process, TimeSpan delay, CancellationToken cancellationToken = default);

    void ScheduleSetTitle(Process process, string titleTemplate, TimeSpan delay, CancellationToken cancellationToken = default);
}
