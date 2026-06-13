using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel
{
    public IReadOnlyList<DownloadSectionItem> Sections { get; } =
    [
        new(DownloadSection.Install, "原版游戏", "下载 Minecraft 原版客户端"),
        new(DownloadSection.Mod, "Mod", "搜索并下载 Mod"),
        new(DownloadSection.ModPack, "整合包", "安装社区整合包"),
        new(DownloadSection.DataPack, "数据包", "搜索并下载数据包"),
        new(DownloadSection.ResourcePack, "资源包", "搜索并下载资源包"),
        new(DownloadSection.Shader, "光影包", "搜索并下载光影包"),
        new(DownloadSection.Manager, "下载管理", "查看当前下载任务")
    ];

    public ObservableCollection<MinecraftRemoteVersion> Versions { get; } = [];

    public ObservableCollection<DownloadVersionCategoryItem> VersionCategoryItems { get; } = [];

    public ObservableCollection<DownloadTaskSnapshot> DownloadTasks { get; } = [];

    public ObservableCollection<CommunityResourceProject> ResourceProjects { get; } = [];

    public ObservableCollection<CommunityResourceVersion> ResourceVersions { get; } = [];

    public ObservableCollection<LoaderVersionOption> LoaderVersions { get; } = [];

    public ObservableCollection<MinecraftRootFolder> MinecraftRootFolders { get; } = [];

    public IAsyncRelayCommand RefreshVersionsCommand { get; private set; } = null!;

    public IAsyncRelayCommand RefreshLoaderVersionsCommand { get; private set; } = null!;

    public IAsyncRelayCommand InstallSelectedVersionCommand { get; private set; } = null!;

    public IAsyncRelayCommand InstallSelectedLoaderCommand { get; private set; } = null!;

    public IRelayCommand BrowseMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand RemoveMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand RenameMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand OpenMinecraftRootCommand { get; private set; } = null!;

    public IAsyncRelayCommand RefreshCurrentSectionCommand { get; private set; } = null!;

    public IRelayCommand RefreshTaskSnapshotsCommand { get; private set; } = null!;

    public IAsyncRelayCommand SearchResourcesCommand { get; private set; } = null!;

    public IAsyncRelayCommand LoadResourceVersionsCommand { get; private set; } = null!;

    public IAsyncRelayCommand DownloadSelectedResourceFileCommand { get; private set; } = null!;

    public IAsyncRelayCommand InstallLocalModpackCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedResourceProjectCommand { get; private set; } = null!;

    public IRelayCommand CancelSelectedDownloadTaskCommand { get; private set; } = null!;

    public IAsyncRelayCommand RetrySelectedDownloadTaskCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedDownloadTaskFolderCommand { get; private set; } = null!;

    public IRelayCommand CancelAllRunningDownloadTasksCommand { get; private set; } = null!;

    public IRelayCommand ClearFinishedDownloadTasksCommand { get; private set; } = null!;

    public IRelayCommand OpenDownloadManagerCommand { get; private set; } = null!;

    public IReadOnlyList<string> ResourceLoaderOptions { get; } = ["", "forge", "fabric", "quilt", "neoforge"];

    public IReadOnlyList<string> ResourceSourceOptions { get; } = ["全部", "Modrinth", "CurseForge"];

    public IReadOnlyList<string> LoaderInstallOptions { get; } = ["Fabric", "Quilt", "Forge", "NeoForge"];

    public IReadOnlyList<string> InstallModeOptions { get; } = ["原版安装", "加载器安装"];

    public IReadOnlyList<string> VersionCategoryOptions { get; } = ["全部版本", "正式版", "快照版", "远古版"];

    public override async Task OnNavigatedToAsync()
    {
        SyncMinecraftRootPathFromSettings();
        ApplyPendingPreset();
        var statusBeforeAutoRefresh = StatusMessage;
        if (await EnsureVersionManifestLoadedAsync())
        {
            if (SelectedSection.Section != DownloadSection.Install && statusBeforeAutoRefresh.Contains("已套用", StringComparison.Ordinal))
            {
                StatusMessage = statusBeforeAutoRefresh;
            }
        }
    }

    public Task PreloadVersionManifestAsync()
    {
        return PreloadVersionManifestCoreAsync();
    }

    private async Task<bool> EnsureVersionManifestLoadedAsync()
    {
        if (!_hasLoadedVersionManifest)
        {
            await RefreshVersionsAsync();
            return true;
        }

        if (_hasAppliedVersionManifestToUi)
        {
            return false;
        }

        ApplyLoadedVersionManifestToUi(preserveSelection: false);
        StatusMessage = $"版本列表已刷新：{Versions.Count} 个";
        return true;
    }

    private async Task PreloadVersionManifestCoreAsync()
    {
        if (_hasLoadedVersionManifest)
        {
            return;
        }

        try
        {
            var versions = await _minecraftClientDownload.GetVersionManifestAsync().ConfigureAwait(false);
            _allVersions.Clear();
            _allVersions.AddRange(versions);
            _hasLoadedVersionManifest = true;
        }
        catch (Exception ex)
        {
            _hasLoadedVersionManifest = false;
            _logger.Error(ex, "启动预热 Minecraft 版本列表失败");
            throw;
        }
    }

    public int VersionCount => Versions.Count;

    public int DownloadTaskCount => DownloadTasks.Count;

    public int RunningTaskCount => DownloadTasks.Count(task => task.State == DownloadTaskState.Running);

    public int FailedTaskCount => DownloadTasks.Count(task => task.State == DownloadTaskState.Failed);

    public int FinishedTaskCount => DownloadTasks.Count(task => task.State is DownloadTaskState.Succeeded or DownloadTaskState.Failed or DownloadTaskState.Canceled);

    public double OverallTaskProgress
    {
        get => DownloadTasks.Count == 0
            ? 1
            : Math.Clamp(DownloadTasks.Average(task => task.Progress), 0, 1);
        set { }
    }

    public double OverallTaskProgressValue
    {
        get => OverallTaskProgress;
        set { }
    }

    public string OverallTaskProgressText => OverallTaskProgress > 0.999999
        ? "100 %"
        : $"{OverallTaskProgress * 100:0.00} %";

    public string DownloadedFileCountText
    {
        get
        {
            var finished = DownloadTasks.Sum(task => task.FinishedFiles);
            var total = DownloadTasks.Sum(task => task.TotalFiles);
            return $"{finished} / {total}";
        }
    }

    public string DownloadedBytesText => FormatByteSize(DownloadTasks.Sum(task => task.BytesReceived));

    public bool HasSelectedDownloadTask => SelectedDownloadTask is not null;

    public string SelectedDownloadTaskStateText => SelectedDownloadTask?.State switch
    {
        DownloadTaskState.Waiting => "等待中",
        DownloadTaskState.Running => "运行中",
        DownloadTaskState.Succeeded => "已完成",
        DownloadTaskState.Failed => "失败",
        DownloadTaskState.Canceled => "已取消",
        _ => "未选择"
    };

    public string SelectedDownloadTaskProgressText => SelectedDownloadTask is null
        ? "未选择任务"
        : $"{Math.Clamp(SelectedDownloadTask.Progress, 0, 1) * 100:0.00} %";

    public string SelectedDownloadTaskFileText => SelectedDownloadTask is null
        ? "文件：-"
        : $"文件：{SelectedDownloadTask.FinishedFiles} / {SelectedDownloadTask.TotalFiles}";

    public string SelectedDownloadTaskBytesText => SelectedDownloadTask is null
        ? "已接收：-"
        : "已接收：" + FormatByteSize(SelectedDownloadTask.BytesReceived);

    public string SelectedDownloadTaskPathText => string.IsNullOrWhiteSpace(SelectedDownloadTask?.PrimaryLocalPath)
        ? "位置：-"
        : "位置：" + SelectedDownloadTask.PrimaryLocalPath;

    public string SelectedDownloadTaskMessage => string.IsNullOrWhiteSpace(SelectedDownloadTask?.Message)
        ? "暂无详细信息"
        : SelectedDownloadTask.Message;

    public bool IsInstallSection => SelectedSection.Section == DownloadSection.Install;

    public bool IsManagerSection => SelectedSection.Section == DownloadSection.Manager;

    public bool IsResourceSection => SelectedSection.Section is not DownloadSection.Install and not DownloadSection.Manager;

    public bool IsVanillaInstallMode => SelectedInstallMode == "原版安装";

    public bool IsLoaderInstallMode => SelectedInstallMode == "加载器安装";

    public int ResourceResultCount => ResourceProjects.Count;

    public int ResourceVersionCount => ResourceVersions.Count;

    public int LoaderVersionCount => LoaderVersions.Count;

    public string SectionTitle => SelectedSection.Title;

    public string SectionDescription => SelectedSection.Description;

    public string LoaderInstancePreview => SelectedVersion is null || string.IsNullOrWhiteSpace(LoaderVersion)
        ? "请选择原版版本并填写加载器版本"
        : GetLoaderInstanceName();

    public string ResourcePanelTitle => SelectedSection.Section switch
    {
        DownloadSection.Mod => "Mod 搜索",
        DownloadSection.ModPack => "整合包搜索",
        DownloadSection.DataPack => "数据包搜索",
        DownloadSection.ResourcePack => "资源包搜索",
        DownloadSection.Shader => "光影包搜索",
        _ => "下载功能"
    };

    public string ResourcePanelMessage => SelectedSection.Section switch
    {
        DownloadSection.Mod => "已接入 Modrinth 搜索、版本列表、主文件下载和必需依赖解析。",
        DownloadSection.ModPack => "已接入本地 .mrpack 解析安装、加载器安装计划和 Forge/NeoForge processors。",
        DownloadSection.DataPack => "已接入 Modrinth 搜索与文件下载，默认下载到 datapacks。",
        DownloadSection.ResourcePack => "已接入 Modrinth 搜索与下载到 resourcepacks。",
        DownloadSection.Shader => "已接入 Modrinth 搜索与下载到 shaderpacks。",
        _ => "下载任务会显示进度、成功与失败状态。"
    };

    public string SelectedResourceDetail => SelectedResourceProject is null
        ? "尚未选择资源"
        : $"{SelectedResourceProject.Name} / {SelectedResourceProject.PlatformName} / {SelectedResourceProject.VersionSummary}";

    public string SelectedResourcePlatformText
    {
        get
        {
            if (SelectedResourceProject is null)
            {
                return "平台：未选择";
            }

            var loaders = SelectedResourceProject.Loaders.Count == 0 ? "未知启动平台" : string.Join(" / ", SelectedResourceProject.Loaders);
            return $"平台：{SelectedResourceProject.PlatformName} / {loaders}";
        }
    }

    public string SelectedResourceGameVersionText
    {
        get
        {
            var versions = SelectedResourceVersion?.GameVersions.Count > 0
                ? SelectedResourceVersion.GameVersions
                : SelectedResourceProject?.GameVersions ?? [];
            return versions.Count == 0
                ? "适用版本：未知"
                : "适用版本：" + string.Join(" / ", versions.Take(8)) + (versions.Count > 8 ? " ..." : "");
        }
    }

    public string SelectedResourceLoaderText
    {
        get
        {
            var loaders = SelectedResourceVersion?.Loaders.Count > 0
                ? SelectedResourceVersion.Loaders
                : SelectedResourceProject?.Loaders ?? [];
            return loaders.Count == 0
                ? "启动平台：未知"
                : "启动平台：" + string.Join(" / ", loaders);
        }
    }

    public string SelectedResourceDependencyText
    {
        get
        {
            if (SelectedResourceVersion is null)
            {
                return "依赖项：等待加载版本信息";
            }

            var required = SelectedResourceVersion.Dependencies.Count(item => item.IsRequired);
            var optional = SelectedResourceVersion.Dependencies.Count - required;
            return required == 0 && optional == 0
                ? "依赖项：无"
                : $"依赖项：必需 {required} 个，可选 {optional} 个；必需依赖会联动下载";
        }
    }

    public string SelectedResourceDependencyListText
    {
        get
        {
            if (SelectedResourceVersion is null)
            {
                return "依赖列表：等待加载版本信息";
            }

            if (SelectedResourceVersion.Dependencies.Count == 0)
            {
                return "依赖列表：无";
            }

            var dependencies = SelectedResourceVersion.Dependencies
                .OrderByDescending(item => item.IsRequired)
                .ThenBy(item => item.ProjectId ?? item.VersionId ?? "", StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(item => item.DisplayText);
            var suffix = SelectedResourceVersion.Dependencies.Count > 6 ? $" 等 {SelectedResourceVersion.Dependencies.Count} 个" : "";
            return "依赖列表：" + string.Join("；", dependencies) + suffix;
        }
    }

    public string SelectedResourceFileSummary => SelectedResourceFile is null
        ? "尚未选择可下载文件"
        : $"{SelectedResourceFile.FileName} / {SelectedResourceFile.SizeText}";

    public string SelectedResourceVersionSummary
    {
        get
        {
            if (SelectedResourceVersion is null)
            {
                return "尚未加载资源版本";
            }

            var dependencyCount = SelectedResourceVersion.Dependencies.Count(item => item.IsRequired);
            var dependencyText = dependencyCount == 0 ? "无必需依赖" : $"{dependencyCount} 个必需依赖会一并下载";
            return $"{SelectedResourceVersion.VersionNumber} / {SelectedResourceVersion.Published:yyyy-MM-dd} / {dependencyText}";
        }
    }

    public bool CanDownloadSelectedResourceFile =>
        !IsBusy
        && SelectedResourceProject is not null
        && SelectedResourceVersion is not null
        && SelectedResourceFile is not null
        && !string.IsNullOrWhiteSpace(SelectedResourceFile.Url);

    public string ResourceDownloadActionText
    {
        get
        {
            if (SelectedResourceFile is null)
            {
                return "选择文件后下载";
            }

            if (string.IsNullOrWhiteSpace(SelectedResourceFile.Url))
            {
                return "文件缺少下载地址";
            }

            var primaryState = GetSelectedResourcePrimaryFileState();
            if (primaryState == ResourcePrimaryFileState.Existing)
            {
                return "文件已存在";
            }

            if (primaryState == ResourcePrimaryFileState.Queued)
            {
                return "已在下载队列";
            }

            var requiredDependencyCount = SelectedResourceVersion?.Dependencies.Count(item => item.IsRequired) ?? 0;
            if (requiredDependencyCount > 0)
            {
                return $"下载并联动 {requiredDependencyCount} 个依赖";
            }

            return SelectedResourceProject?.Type == CommunityResourceType.ModPack
                ? "下载整合包文件"
                : "下载到目标目录";
        }
    }

    public string SelectedResourceDownloadPlanText
    {
        get
        {
            if (SelectedResourceProject is null)
            {
                return "下载计划：请选择资源。";
            }

            if (SelectedResourceVersion is null)
            {
                return "下载计划：版本信息会自动加载；选择版本后会显示依赖和重复检测状态。";
            }

            if (SelectedResourceFile is null)
            {
                return "下载计划：请选择要下载的文件。";
            }

            if (string.IsNullOrWhiteSpace(SelectedResourceFile.Url))
            {
                return "下载计划：所选文件缺少下载地址，无法创建任务。";
            }

            var requiredDependencyCount = SelectedResourceVersion.Dependencies.Count(item => item.IsRequired);
            var optionalDependencyCount = SelectedResourceVersion.Dependencies.Count - requiredDependencyCount;
            var dependencyText = requiredDependencyCount == 0 && optionalDependencyCount == 0
                ? "依赖计划：无依赖。"
                : $"依赖计划：{requiredDependencyCount} 个必需依赖会联动下载，{optionalDependencyCount} 个可选依赖仅提示不自动下载。";
            var primaryStateText = GetSelectedResourcePrimaryFileState() switch
            {
                ResourcePrimaryFileState.Existing => "主文件状态：目标位置已有匹配文件，会跳过重复下载。",
                ResourcePrimaryFileState.Queued => "主文件状态：相同目标文件已在下载队列中。",
                ResourcePrimaryFileState.UnknownTarget => "主文件状态：目标目录需要在创建任务时确认。",
                _ => "主文件状态：将创建下载任务。"
            };

            return $"下载计划：{SelectedResourceProject.PlatformName} / {GetResourceTypeText(SelectedResourceProject.Type)}\n{primaryStateText}\n{dependencyText}\n重复检测：创建任务时会再次检查主文件和必需依赖，避免重复下载。";
        }
    }

    public string DownloadInfoTitle => SelectedSection.Section switch
    {
        DownloadSection.Install => "原版下载信息",
        DownloadSection.Manager => "任务概览",
        DownloadSection.Mod => "Mod 下载信息",
        DownloadSection.ModPack => "整合包下载信息",
        DownloadSection.DataPack => "数据包下载信息",
        DownloadSection.ResourcePack => "资源包下载信息",
        DownloadSection.Shader => "光影包下载信息",
        _ => "下载信息"
    };

    public string DownloadInfoSummary => SelectedSection.Section switch
    {
        DownloadSection.Install => SelectedVersion is null
            ? "版本列表会在打开下载页时自动刷新一次。"
            : $"{SelectedVersion.Id} / {SelectedVersion.TypeText} / {SelectedVersion.ReleaseTime:yyyy-MM-dd HH:mm}",
        DownloadSection.Manager => $"任务：{DownloadTaskCount} 个，运行中 {RunningTaskCount} 个，失败 {FailedTaskCount} 个",
        _ => SelectedResourceProject is null
            ? "请选择或搜索一个资源，版本信息会自动加载。"
            : $"{SelectedResourceProject.Name} / {SelectedResourceProject.PlatformName}"
    };

    public string DownloadInfoDetails => SelectedSection.Section switch
    {
        DownloadSection.Install => $"分类：{SelectedVersionCategory}\n版本类型：{SelectedVersion?.TypeText ?? "未选择"}\n安装方式：{SelectedInstallMode}\n目标实例：{InstanceVersionSafeName}\n版本数：{VersionCount}\n列表会在启动器打开后自动预热一次，手动刷新仍可重新获取。",
        DownloadSection.Manager => $"总体进度：{OverallTaskProgressText}\n文件：{DownloadedFileCountText}\n已接收：{DownloadedBytesText}\n任务列表、取消、重试和打开位置请在“下载管理”页操作。",
        _ => $"{SelectedResourceDetail}\n{SelectedResourcePlatformText}\n{SelectedResourceGameVersionText}\n{SelectedResourceLoaderText}\n{SelectedResourceVersionSummary}\n{SelectedResourceDependencyText}\n{SelectedResourceDependencyListText}\n{SelectedResourceFileSummary}\n{SelectedResourceDownloadPlanText}\n{ResourceInstallTarget}"
    };

    private ResourcePrimaryFileState GetSelectedResourcePrimaryFileState()
    {
        if (SelectedResourceProject is null || SelectedResourceVersion is null || SelectedResourceFile is null)
        {
            return ResourcePrimaryFileState.UnknownTarget;
        }

        if (string.IsNullOrWhiteSpace(MinecraftRootPath))
        {
            return ResourcePrimaryFileState.UnknownTarget;
        }

        try
        {
            var file = _communityResourceVersions.CreateDownloadFile(
                SelectedResourceProject,
                SelectedResourceVersion,
                SelectedResourceFile,
                MinecraftRootPath);
            if (IsExistingDownloadSatisfied(file))
            {
                return ResourcePrimaryFileState.Existing;
            }

            if (IsDownloadAlreadyQueued(file))
            {
                return ResourcePrimaryFileState.Queued;
            }

            return ResourcePrimaryFileState.Pending;
        }
        catch
        {
            return ResourcePrimaryFileState.UnknownTarget;
        }
    }

    private static string GetResourceTypeText(CommunityResourceType type)
    {
        return type switch
        {
            CommunityResourceType.Mod => "Mod",
            CommunityResourceType.ModPack => "整合包",
            CommunityResourceType.DataPack => "数据包",
            CommunityResourceType.ResourcePack => "资源包",
            CommunityResourceType.Shader => "光影包",
            _ => "社区资源"
        };
    }

    private enum ResourcePrimaryFileState
    {
        Pending,
        Existing,
        Queued,
        UnknownTarget
    }

}

