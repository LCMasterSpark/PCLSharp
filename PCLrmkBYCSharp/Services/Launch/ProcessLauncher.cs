using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class ProcessLauncher : IProcessLauncher
{
    public Process Start(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo) ?? throw new InvalidOperationException("启动游戏进程失败");
    }

    public async Task<int> WaitForExitAsync(Process process, CancellationToken cancellationToken = default)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
