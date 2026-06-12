using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Services;

public interface IAppExitGuardService
{
    bool CanExit();
}

public sealed class AppExitGuardService(
    IDownloadManagerService downloadManager,
    IUserPromptService prompts,
    IAppLoggerService logger) : IAppExitGuardService
{
    public bool CanExit()
    {
        var runningTasks = downloadManager.Tasks
            .Where(task => task.State == DownloadTaskState.Running)
            .ToList();
        if (runningTasks.Count == 0)
        {
            return true;
        }

        if (!prompts.Confirm("提示", $"还有 {runningTasks.Count} 个下载任务尚未完成，是否确定退出？"))
        {
            logger.Info("用户取消退出：仍有下载任务运行中");
            return false;
        }

        logger.Info("正在取消运行中的下载任务");
        foreach (var task in runningTasks)
        {
            downloadManager.Cancel(task.Name);
        }

        return true;
    }
}

public sealed class AlwaysAllowExitGuardService : IAppExitGuardService
{
    public bool CanExit()
    {
        return true;
    }
}
