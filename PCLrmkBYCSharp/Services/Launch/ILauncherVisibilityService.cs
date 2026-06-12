using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILauncherVisibilityService
{
    void ApplyAfterLaunch(int launcherVisibility, Process gameProcess, CancellationToken cancellationToken = default);
}
