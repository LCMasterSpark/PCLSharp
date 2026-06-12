using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class WindowsLaunchMemoryOptimizer(IAppLoggerService logger) : ILaunchMemoryOptimizer
{
    public Task<LaunchMemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            logger.Info("启动前内存优化开始");
            var optimized = 0;
            foreach (var process in Process.GetProcesses())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (process)
                {
                    try
                    {
                        if (EmptyWorkingSet(process.Handle))
                        {
                            optimized++;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        // 部分系统进程拒绝访问是正常情况，旧版也是尽力优化。
                    }
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            logger.Info("启动前内存优化完成，已处理进程数：" + optimized);
            return new LaunchMemoryOptimizeResult(optimized);
        }, cancellationToken);
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);
}
