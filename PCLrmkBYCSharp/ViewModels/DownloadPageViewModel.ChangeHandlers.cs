using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel
{
    partial void OnSelectedVersionChanged(MinecraftRemoteVersion? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(InstanceName))
        {
            InstanceName = value.Id;
        }

        OnPropertyChanged(nameof(LoaderInstancePreview));
        NotifyDownloadInfoChanged();
    }

    partial void OnSelectedVersionCategoryChanged(string value)
    {
        ApplyVersionCategoryFilter(preserveSelection: true);
        StatusMessage = "原版版本分类：" + value;
        NotifyDownloadInfoChanged();
    }

    partial void OnSelectedLoaderKindChanged(string value)
    {
        LoaderVersions.Clear();
        SelectedLoaderVersion = null;
        OnPropertyChanged(nameof(LoaderVersionCount));
        OnPropertyChanged(nameof(LoaderInstancePreview));
    }

    partial void OnLoaderVersionChanged(string value)
    {
        OnPropertyChanged(nameof(LoaderInstancePreview));
    }

    partial void OnSelectedLoaderVersionChanged(LoaderVersionOption? value)
    {
        if (value is not null)
        {
            LoaderVersion = value.Version;
            StatusMessage = "已选择加载器版本：" + value.Version;
        }
    }

    partial void OnSelectedSectionChanged(DownloadSectionItem value)
    {
        OnPropertyChanged(nameof(IsInstallSection));
        OnPropertyChanged(nameof(IsManagerSection));
        OnPropertyChanged(nameof(IsResourceSection));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
        OnPropertyChanged(nameof(ResourcePanelTitle));
        OnPropertyChanged(nameof(ResourcePanelMessage));
        UpdateResourceInstallTargetLabel();
        NotifySelectedResourceDownloadStateChanged();
        NotifyDownloadInfoChanged();
        StatusMessage = value.Section switch
        {
            DownloadSection.Install => "原版游戏下载页已就绪",
            DownloadSection.Manager => $"下载管理：{DownloadTaskCount} 个任务",
            _ => $"{value.Title} 搜索页已就绪"
        };
    }

    partial void OnSelectedInstallModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsVanillaInstallMode));
        OnPropertyChanged(nameof(IsLoaderInstallMode));
        StatusMessage = "安装子页：" + value;
        NotifyDownloadInfoChanged();
    }

    partial void OnSelectedResourceProjectChanged(CommunityResourceProject? value)
    {
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        SelectedResourceFile = null;
        OpenSelectedResourceProjectCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedResourceDetail));
        OnPropertyChanged(nameof(ResourceVersionCount));
        UpdateResourceInstallTargetLabel();
        NotifySelectedResourceDownloadStateChanged();
        if (value is not null)
        {
            StatusMessage = $"已选择资源：{value.Name}";
            AutoLoadResourceVersionsForSelection();
        }
    }

    partial void OnSelectedResourceVersionChanged(CommunityResourceVersion? value)
    {
        SelectedResourceFile = value?.PrimaryFile;
        NotifySelectedResourceDownloadStateChanged();
    }

    partial void OnSelectedResourceFileChanged(CommunityResourceFile? value)
    {
        NotifySelectedResourceDownloadStateChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifySelectedResourceDownloadStateChanged();
    }

    partial void OnSelectedResourceSourceChanged(string value)
    {
        ApplyResourceSourceFilter();
        StatusMessage = "资源来源筛选：" + value;
        AutoLoadResourceVersionsForSelection();
    }

    partial void OnResourceGameVersionChanged(string value)
    {
        ResetResourceVersionsForFilterChange();
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "已清空 Minecraft 版本筛选"
            : "Minecraft 版本筛选：" + value;
        AutoLoadResourceVersionsForSelection();
    }

    partial void OnResourceLoaderChanged(string value)
    {
        ResetResourceVersionsForFilterChange();
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "已清空启动平台筛选"
            : "启动平台筛选：" + value;
        AutoLoadResourceVersionsForSelection();
    }

    partial void OnSelectedDownloadTaskChanged(DownloadTaskSnapshot? value)
    {
        CancelSelectedDownloadTaskCommand?.NotifyCanExecuteChanged();
        RetrySelectedDownloadTaskCommand?.NotifyCanExecuteChanged();
        OpenSelectedDownloadTaskFolderCommand?.NotifyCanExecuteChanged();
        NotifySelectedDownloadTaskDetailsChanged();
        NotifyDownloadInfoChanged();
    }

    partial void OnMinecraftRootPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settings.Set(AppSettingKeys.MinecraftRootPath, value);
            _settings.Set(AppSettingKeys.LaunchFolderSelect, value);
            RefreshMinecraftRootFolders();
            UpdateResourceInstallTargetLabel();
            NotifySelectedResourceDownloadStateChanged();
        }
    }

    partial void OnSelectedMinecraftRootFolderChanged(MinecraftRootFolder? value)
    {
        RemoveMinecraftRootCommand?.NotifyCanExecuteChanged();
        RenameMinecraftRootCommand?.NotifyCanExecuteChanged();
        OpenMinecraftRootCommand?.NotifyCanExecuteChanged();
        if (_isSyncingRootFolderSelection || value is null)
        {
            return;
        }

        if (!string.Equals(MinecraftRootPath, value.Path, StringComparison.OrdinalIgnoreCase))
        {
            MinecraftRootPath = value.Path;
            UpdateResourceInstallTargetLabel();
            StatusMessage = "下载目标已切换到 " + value.Name;
            NotifyDownloadInfoChanged();
        }
    }

    private void ResetResourceVersionsForFilterChange()
    {
        ResourceVersions.Clear();
        SelectedResourceVersion = null;
        SelectedResourceFile = null;
        OnPropertyChanged(nameof(ResourceVersionCount));
        NotifySelectedResourceDownloadStateChanged();
    }
}
