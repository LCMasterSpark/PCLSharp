using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IProcessLauncher
{
    Process Start(ProcessStartInfo startInfo);

    Task<int> WaitForExitAsync(Process process, CancellationToken cancellationToken = default);
}
