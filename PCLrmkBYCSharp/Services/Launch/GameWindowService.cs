using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class GameWindowService(IAppLoggerService logger) : IGameWindowService
{
    private const int ShowMaximized = 3;

    public void ScheduleMaximize(Process process, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                var handle = await WaitForMainWindowAsync(process, cancellationToken).ConfigureAwait(false);
                if (handle == IntPtr.Zero)
                {
                    logger.Warn("未找到 Minecraft 窗口，跳过最大化");
                    return;
                }

                if (ShowWindow(handle, ShowMaximized))
                {
                    logger.Info($"已最大化 Minecraft 窗口：{handle.ToInt64()}");
                }
                else
                {
                    logger.Warn($"最大化 Minecraft 窗口失败：{handle.ToInt64()}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Warn("最大化 Minecraft 窗口时出现错误：" + ex.Message);
            }
        }, CancellationToken.None);
    }

    public void ScheduleSetTitle(Process process, string titleTemplate, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(titleTemplate))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                var handle = await WaitForMainWindowAsync(process, cancellationToken).ConfigureAwait(false);
                if (handle == IntPtr.Zero)
                {
                    logger.Warn("未找到 Minecraft 窗口，跳过设置窗口标题");
                    return;
                }

                for (var i = 0; i < 3; i++)
                {
                    var title = ReplaceTimeTokens(titleTemplate);
                    SetWindowText(handle, title);
                    await Task.Delay(64, cancellationToken).ConfigureAwait(false);
                }

                logger.Info($"已设置 Minecraft 窗口标题：{titleTemplate}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Warn("设置 Minecraft 窗口标题时出现错误：" + ex.Message);
            }
        }, CancellationToken.None);
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return IntPtr.Zero;
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    private static string ReplaceTimeTokens(string titleTemplate)
    {
        var now = DateTime.Now;
        return titleTemplate
            .Replace("{date}", now.ToString("yyyy/M/d"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HH:mm:ss"), StringComparison.OrdinalIgnoreCase);
    }
}
