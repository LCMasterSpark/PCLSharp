using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LauncherVisibilityService(IAppLoggerService logger, ILauncherWindowHost windowHost) : ILauncherVisibilityService
{
    public void ApplyAfterLaunch(int launcherVisibility, Process gameProcess, CancellationToken cancellationToken = default)
    {
        logger.Info("启动器可见性：" + launcherVisibility);
        switch (launcherVisibility)
        {
            case 0:
                logger.Info("已根据设置，在启动后关闭启动器");
                windowHost.Close();
                break;
            case 2:
                logger.Info("已根据设置，在启动后隐藏启动器，游戏退出后自动关闭");
                windowHost.Hide();
                RunAfterGameExit(gameProcess, windowHost.Close, cancellationToken);
                break;
            case 3:
                logger.Info("已根据设置，在启动后隐藏启动器，游戏退出后重新打开");
                windowHost.Hide();
                RunAfterGameExit(gameProcess, windowHost.ShowToTop, cancellationToken);
                break;
            case 4:
                logger.Info("已根据设置，在启动后最小化启动器");
                windowHost.Minimize();
                break;
            case 5:
                break;
        }
    }

    private void RunAfterGameExit(Process process, Action action, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Warn("处理游戏退出后的启动器可见性失败：" + ex.Message);
            }
        }, CancellationToken.None);
    }
}
