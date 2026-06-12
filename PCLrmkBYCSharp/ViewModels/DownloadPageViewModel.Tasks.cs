using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel
{
    private void RefreshTaskSnapshots()
    {
        if (ShouldMarshalDirectCollectionUpdate())
        {
            RefreshTaskSnapshotsOnUi();
            return;
        }

        var selectedName = SelectedDownloadTask?.Name;
        var snapshots = _downloadManager.Tasks.ToArray();
        DownloadTaskSnapshot? nextSelected;

        lock (_downloadTasksSync)
        {
            for (var index = DownloadTasks.Count - 1; index >= 0; index--)
            {
                if (snapshots.All(snapshot => !IsSameTask(snapshot, DownloadTasks[index])))
                {
                    DownloadTasks.RemoveAt(index);
                }
            }

            for (var targetIndex = 0; targetIndex < snapshots.Length; targetIndex++)
            {
                var snapshot = snapshots[targetIndex];
                var currentIndex = IndexOfDownloadTask(snapshot.Name);
                if (currentIndex < 0)
                {
                    DownloadTasks.Insert(targetIndex, snapshot);
                    continue;
                }

                if (currentIndex != targetIndex)
                {
                    DownloadTasks.Move(currentIndex, targetIndex);
                }

                if (!Equals(DownloadTasks[targetIndex], snapshot))
                {
                    DownloadTasks[targetIndex] = snapshot;
                }
            }

            nextSelected = string.IsNullOrWhiteSpace(selectedName)
                ? DownloadTasks.FirstOrDefault()
                : DownloadTasks.FirstOrDefault(task => string.Equals(task.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    ?? DownloadTasks.FirstOrDefault();
        }

        SelectedDownloadTask = nextSelected;
        OnPropertyChanged(nameof(DownloadTaskCount));
        OnPropertyChanged(nameof(RunningTaskCount));
        OnPropertyChanged(nameof(FailedTaskCount));
        OnPropertyChanged(nameof(FinishedTaskCount));
        OnPropertyChanged(nameof(OverallTaskProgress));
        OnPropertyChanged(nameof(OverallTaskProgressValue));
        OnPropertyChanged(nameof(OverallTaskProgressText));
        OnPropertyChanged(nameof(DownloadedFileCountText));
        OnPropertyChanged(nameof(DownloadedBytesText));
        NotifySelectedDownloadTaskDetailsChanged();
        NotifyDownloadInfoChanged();
        CancelSelectedDownloadTaskCommand.NotifyCanExecuteChanged();
        RetrySelectedDownloadTaskCommand.NotifyCanExecuteChanged();
        OpenSelectedDownloadTaskFolderCommand.NotifyCanExecuteChanged();
        CancelAllRunningDownloadTasksCommand.NotifyCanExecuteChanged();
        ClearFinishedDownloadTasksCommand.NotifyCanExecuteChanged();
    }

    private void RefreshTaskSnapshotsOnUi()
    {
        if (IsOnUiThread())
        {
            TryRefreshTaskSnapshots();
            return;
        }

        if (_dispatcher is not null)
        {
            _ = DispatchTaskSnapshotsAsync();
            return;
        }

        var appDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            _ = appDispatcher.InvokeAsync(TryRefreshTaskSnapshots);
            return;
        }

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => TryRefreshTaskSnapshots(), null);
            return;
        }

        TryRefreshTaskSnapshots();
    }

    private async Task DispatchTaskSnapshotsAsync()
    {
        try
        {
            await _dispatcher!.InvokeAsync(TryRefreshTaskSnapshots);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "刷新下载任务列表失败");
        }
    }

    private void TryRefreshTaskSnapshots()
    {
        try
        {
            RefreshTaskSnapshots();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "刷新下载任务列表失败");
        }
    }

    private bool IsOnUiThread()
    {
        if (_dispatcher is not null)
        {
            return _dispatcher.CheckAccess();
        }

        var appDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (appDispatcher is not null)
        {
            return appDispatcher.CheckAccess();
        }

        return _uiContext is null || SynchronizationContext.Current == _uiContext;
    }

    private bool ShouldMarshalDirectCollectionUpdate()
    {
        var appDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            return true;
        }

        return _uiContext is not null && SynchronizationContext.Current != _uiContext;
    }

    private void NotifySelectedDownloadTaskDetailsChanged()
    {
        OnPropertyChanged(nameof(HasSelectedDownloadTask));
        OnPropertyChanged(nameof(SelectedDownloadTaskStateText));
        OnPropertyChanged(nameof(SelectedDownloadTaskProgressText));
        OnPropertyChanged(nameof(SelectedDownloadTaskFileText));
        OnPropertyChanged(nameof(SelectedDownloadTaskBytesText));
        OnPropertyChanged(nameof(SelectedDownloadTaskPathText));
        OnPropertyChanged(nameof(SelectedDownloadTaskMessage));
    }

    private int IndexOfDownloadTask(string name)
    {
        for (var index = 0; index < DownloadTasks.Count; index++)
        {
            if (string.Equals(DownloadTasks[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSameTask(DownloadTaskSnapshot left, DownloadTaskSnapshot right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatByteSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => bytes + " B",
            < 1024 * 1024 => (bytes / 1024D).ToString("0.#") + " KB",
            < 1024L * 1024 * 1024 => (bytes / 1024D / 1024D).ToString("0.#") + " MB",
            _ => (bytes / 1024D / 1024D / 1024D).ToString("0.##") + " GB"
        };
    }

    private void CancelSelectedDownloadTask()
    {
        if (SelectedDownloadTask is null)
        {
            StatusMessage = "请先选择一个下载任务";
            return;
        }

        StatusMessage = _downloadManager.Cancel(SelectedDownloadTask.Name)
            ? "已请求取消下载任务：" + SelectedDownloadTask.Name
            : "当前任务不可取消";
        RefreshTaskSnapshots();
    }

    private void CancelAllRunningDownloadTasks()
    {
        var count = _downloadManager.CancelAllRunning();
        RefreshTaskSnapshots();
        StatusMessage = count == 0
            ? "当前没有运行中的下载任务"
            : $"已请求取消 {count} 个下载任务";
    }

    private void ClearFinishedDownloadTasks()
    {
        var count = _downloadManager.ClearFinished();
        RefreshTaskSnapshots();
        StatusMessage = count == 0
            ? "没有可清理的已结束任务"
            : $"已清理 {count} 个已结束任务";
    }

    public async Task RetrySelectedDownloadTaskAsync()
    {
        if (SelectedDownloadTask is null)
        {
            StatusMessage = "请先选择一个下载任务";
            return;
        }

        await RunBusyAsync("正在重试下载任务...", async () =>
        {
            var snapshot = await _downloadManager.RetryAsync(SelectedDownloadTask.Name);
            RefreshTaskSnapshots();
            StatusMessage = snapshot is null
                ? "该下载任务没有可重试的文件列表"
                : "已重试下载任务：" + snapshot.Name + "，" + snapshot.Message;
        });
    }

    private void OpenSelectedDownloadTaskFolder()
    {
        if (SelectedDownloadTask is null || string.IsNullOrWhiteSpace(SelectedDownloadTask.PrimaryLocalPath))
        {
            StatusMessage = "请先选择一个有本地文件路径的任务";
            return;
        }

        var folder = Path.GetDirectoryName(SelectedDownloadTask.PrimaryLocalPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusMessage = "无法定位下载任务文件夹";
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            _folders.OpenFolder(folder);
            StatusMessage = "已打开下载文件夹";
        }
        catch (Exception ex)
        {
            StatusMessage = "打开下载文件夹失败：" + ex.Message;
            _logger.Error(ex, "打开下载文件夹失败");
        }
    }

    private static CommunityResourceType ToResourceType(DownloadSection section)
    {
        return section switch
        {
            DownloadSection.ModPack => CommunityResourceType.ModPack,
            DownloadSection.DataPack => CommunityResourceType.DataPack,
            DownloadSection.ResourcePack => CommunityResourceType.ResourcePack,
            DownloadSection.Shader => CommunityResourceType.Shader,
            _ => CommunityResourceType.Mod
        };
    }
}
