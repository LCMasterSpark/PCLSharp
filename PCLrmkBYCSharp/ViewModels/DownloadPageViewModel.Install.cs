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
    public async Task InstallLocalModpackAsync()
    {
        var selected = _fileDialogs.PickModpackFile(MinecraftRootPath);
        if (string.IsNullOrWhiteSpace(selected))
        {
            StatusMessage = "\u672a\u9009\u62e9\u6574\u5408\u5305";
            return;
        }

        await RunBusyAsync("\u6b63\u5728\u89e3\u6790\u6574\u5408\u5305...", async () =>
        {
            _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
            var plan = await _modpackInstall.CreateModrinthInstallPlanAsync(selected, MinecraftRootPath);
            var existingSkippedCount = 0;
            var queuedSkippedCount = 0;
            var files = plan.Files
                .DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase)
                .Where(file =>
                {
                    if (IsExistingDownloadSatisfied(file))
                    {
                        existingSkippedCount++;
                        return false;
                    }

                    if (IsDownloadAlreadyQueued(file))
                    {
                        queuedSkippedCount++;
                        return false;
                    }

                    return true;
                })
                .ToList();
            var downloadMessage = "\u6ca1\u6709\u9700\u8981\u4e0b\u8f7d\u7684\u6587\u4ef6";
            if (files.Count > 0)
            {
                var snapshot = await _downloadManager.DownloadAsync("\u6574\u5408\u5305 " + plan.InstanceName + " \u5b89\u88c5", files);
                downloadMessage = snapshot.Message;
            }
            else if (plan.Files.Count > 0)
            {
                downloadMessage = queuedSkippedCount > 0
                    ? "\u6574\u5408\u5305\u5b89\u88c5\u6587\u4ef6\u5df2\u5b58\u5728\u6216\u6b63\u5728\u4e0b\u8f7d\uff0c\u5df2\u8df3\u8fc7\u91cd\u590d\u4efb\u52a1"
                    : "\u6574\u5408\u5305\u5b89\u88c5\u6587\u4ef6\u5df2\u5b58\u5728";
            }

            var processorMessage = queuedSkippedCount == 0
                ? await RunLoaderProcessorsIfPossibleAsync(plan)
                : "\uff0cprocessors \u7b49\u5f85\u961f\u5217\u4e2d\u4f9d\u8d56\u5b8c\u6210";
            MarkInstalledInstanceSelected(plan.InstanceName);
            RefreshTaskSnapshots();
            var warning = plan.Warnings.Count == 0 ? "" : "\uff0c" + string.Join("\uff1b", plan.Warnings);
            var skippedCount = existingSkippedCount + queuedSkippedCount;
            var skippedText = skippedCount == 0
                ? ""
                : $"\uff0c\u5df2\u8df3\u8fc7 {existingSkippedCount} \u4e2a\u5df2\u5b58\u5728\u6587\u4ef6\u3001{queuedSkippedCount} \u4e2a\u961f\u5217\u4e2d\u4efb\u52a1";
            StatusMessage = $"{downloadMessage}{skippedText}\uff0c\u5df2\u5b89\u88c5 {plan.Name}\uff0c\u5df2\u8bbe\u4e3a\u5f53\u524d\u7248\u672c\uff0c\u8986\u76d6\u6587\u4ef6 {plan.OverrideFileCount} \u4e2a{processorMessage}{warning}";
        });
    }

    private async Task<string> RunLoaderProcessorsIfPossibleAsync(ModpackInstallPlan plan)
    {
        if (plan.ProcessorSteps.Count == 0)
        {
            return "";
        }

        var javaPath = _settings.Get(AppSettingKeys.LaunchJavaPath, "");
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
        {
            return "\uff0cForge/NeoForge processors \u7b49\u5f85 Java \u8def\u5f84";
        }

        var result = await _processorRunner.RunAsync(MinecraftRootPath, javaPath, plan.ProcessorSteps);
        if (result.Success)
        {
            return $"\uff0cprocessors \u5df2\u6267\u884c {result.ExecutedProcessors.Count} \u4e2a";
        }

        var issueCount = result.MissingInputs.Count + result.MissingOutputs.Count + result.FailedProcessors.Count;
        return $"\uff0cprocessors \u672a\u5b8c\u6210 {issueCount} \u9879";
    }

    public async Task RefreshVersionsAsync()
    {
        await RunBusyAsync("正在获取 Minecraft 版本列表...", async () =>
        {
            var versions = await _minecraftClientDownload.GetVersionManifestAsync();
            _allVersions.Clear();
            _allVersions.AddRange(versions);
            _hasLoadedVersionManifest = true;
            ApplyLoadedVersionManifestToUi(preserveSelection: false);

            StatusMessage = $"版本列表已刷新：{Versions.Count} 个";
        });
    }

    private void ApplyLoadedVersionManifestToUi(bool preserveSelection)
    {
        UpdateVersionCategoryItems();
        ApplyVersionCategoryFilter(preserveSelection);
        if (SelectedVersion is not null)
        {
            InstanceName = SelectedVersion.Id;
        }

        _hasAppliedVersionManifestToUi = true;
        NotifyDownloadInfoChanged();
    }

    private void ApplyVersionCategoryFilter(bool preserveSelection)
    {
        var selectedId = preserveSelection ? SelectedVersion?.Id : null;
        IEnumerable<MinecraftRemoteVersion> filtered = _allVersions;
        filtered = SelectedVersionCategory switch
        {
            "正式版" => filtered.Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase)),
            "快照版" => filtered.Where(version => string.Equals(version.Type, "snapshot", StringComparison.OrdinalIgnoreCase)),
            "远古版" => filtered.Where(version =>
                string.Equals(version.Type, "old_alpha", StringComparison.OrdinalIgnoreCase)
                || string.Equals(version.Type, "old_beta", StringComparison.OrdinalIgnoreCase)),
            _ => filtered
        };

        Versions.Clear();
        foreach (var version in filtered)
        {
            Versions.Add(version);
        }

        OnPropertyChanged(nameof(VersionCount));
        SelectedVersion = !string.IsNullOrWhiteSpace(selectedId)
            ? Versions.FirstOrDefault(version => string.Equals(version.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? Versions.FirstOrDefault()
            : Versions.FirstOrDefault();
    }

    private void UpdateVersionCategoryItems()
    {
        var releaseCount = _allVersions.Count(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase));
        var snapshotCount = _allVersions.Count(version => string.Equals(version.Type, "snapshot", StringComparison.OrdinalIgnoreCase));
        var oldCount = _allVersions.Count(version =>
            string.Equals(version.Type, "old_alpha", StringComparison.OrdinalIgnoreCase)
            || string.Equals(version.Type, "old_beta", StringComparison.OrdinalIgnoreCase));

        VersionCategoryItems.Clear();
        VersionCategoryItems.Add(new DownloadVersionCategoryItem("全部版本", "完整版本列表", _allVersions.Count));
        VersionCategoryItems.Add(new DownloadVersionCategoryItem("正式版", "稳定发布版本", releaseCount));
        VersionCategoryItems.Add(new DownloadVersionCategoryItem("快照版", "每周测试版本", snapshotCount));
        VersionCategoryItems.Add(new DownloadVersionCategoryItem("远古版", "Alpha / Beta", oldCount));
    }

    public async Task RefreshLoaderVersionsAsync()
    {
        if (SelectedVersion is null)
        {
            StatusMessage = "请先选择一个 Minecraft 版本";
            return;
        }

        if (_loaderVersions is null)
        {
            StatusMessage = "加载器版本服务未初始化";
            return;
        }

        await RunBusyAsync("正在获取加载器版本列表...", async () =>
        {
            var versions = await _loaderVersions.GetVersionsAsync(SelectedLoaderKind, SelectedVersion.Id);
            LoaderVersions.Clear();
            foreach (var version in versions)
            {
                LoaderVersions.Add(version);
            }

            SelectedLoaderVersion = LoaderVersions.FirstOrDefault();
            OnPropertyChanged(nameof(LoaderVersionCount));
            StatusMessage = LoaderVersions.Count == 0
                ? $"{SelectedLoaderKind} 暂无可用版本，可手动填写加载器版本"
                : $"已获取 {LoaderVersions.Count} 个 {SelectedLoaderKind} 版本";
        });
    }

    public async Task InstallSelectedVersionAsync()
    {
        if (SelectedVersion is null)
        {
            StatusMessage = "请先选择一个 Minecraft 版本";
            return;
        }

        await RunBusyAsync("正在准备原版下载任务...", async () =>
        {
            _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
            var plan = await _minecraftClientDownload.CreateInstallPlanAsync(
                MinecraftRootPath,
                SelectedVersion.Id,
                InstanceVersionSafeName);

            var existingSkippedCount = 0;
            var queuedSkippedCount = 0;
            var files = plan.Files
                .Where(file =>
                {
                    if (IsExistingDownloadSatisfied(file))
                    {
                        existingSkippedCount++;
                        return false;
                    }

                    if (IsDownloadAlreadyQueued(file))
                    {
                        queuedSkippedCount++;
                        return false;
                    }

                    return true;
                })
                .ToList();
            if (files.Count == 0)
            {
                MarkInstalledInstanceSelected(plan.InstanceName);
                RefreshTaskSnapshots();
                StatusMessage = queuedSkippedCount > 0
                    ? "原版安装文件已存在或正在下载，已跳过重复任务并设为当前版本"
                    : "原版安装文件已存在，已设为当前版本";
                return;
            }

            var snapshot = await _downloadManager.DownloadAsync("Minecraft " + plan.InstanceName + " 下载", files);
            MarkInstalledInstanceSelected(plan.InstanceName);
            RefreshTaskSnapshots();
            var skippedCount = existingSkippedCount + queuedSkippedCount;
            var skippedText = skippedCount == 0
                ? ""
                : $"，已跳过 {existingSkippedCount} 个已存在文件、{queuedSkippedCount} 个队列中任务";
            StatusMessage = snapshot.Message + skippedText + "，已设为当前版本";
        });
    }

    public async Task InstallSelectedLoaderAsync()
    {
        if (SelectedVersion is null)
        {
            StatusMessage = "请先选择一个 Minecraft 版本";
            return;
        }

        if (string.IsNullOrWhiteSpace(LoaderVersion))
        {
            StatusMessage = "请填写加载器版本";
            return;
        }

        await RunBusyAsync("正在准备加载器安装任务...", async () =>
        {
            _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
            var instanceName = GetLoaderInstanceName();
            var instancePath = Path.Combine(MinecraftRootPath, "versions", instanceName);
            var vanillaPlan = await _minecraftClientDownload.CreateInstallPlanAsync(
                MinecraftRootPath,
                SelectedVersion.Id,
                SelectedVersion.Id);
            var loaderPlan = await CreateLoaderInstallPlanAsync(instanceName, instancePath);
            var files = vanillaPlan.Files
                .Concat(loaderPlan.Files)
                .DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var existingSkippedCount = 0;
            var queuedSkippedCount = 0;
            files = files
                .Where(file =>
                {
                    if (IsExistingDownloadSatisfied(file))
                    {
                        existingSkippedCount++;
                        return false;
                    }

                    if (IsDownloadAlreadyQueued(file))
                    {
                        queuedSkippedCount++;
                        return false;
                    }

                    return true;
                })
                .ToList();
            if (files.Count == 0)
            {
                var noDownloadProcessorMessage = queuedSkippedCount == 0
                    ? await RunLoaderProcessorsIfPossibleAsync(loaderPlan)
                    : "，processors 等待队列中依赖完成";
                MarkInstalledInstanceSelected(instanceName);
                RefreshTaskSnapshots();
                StatusMessage = queuedSkippedCount > 0
                    ? $"加载器安装文件已存在或正在下载，已跳过重复任务，已安装 {SelectedLoaderKind} {LoaderVersion.Trim()} 并设为当前版本{noDownloadProcessorMessage}"
                    : $"加载器安装文件已存在，已安装 {SelectedLoaderKind} {LoaderVersion.Trim()} 并设为当前版本{noDownloadProcessorMessage}";
                return;
            }

            var snapshot = await _downloadManager.DownloadAsync(
                $"{SelectedLoaderKind} {LoaderVersion.Trim()} for Minecraft {SelectedVersion.Id}",
                files);
            var postDownloadProcessorMessage = queuedSkippedCount == 0
                ? await RunLoaderProcessorsIfPossibleAsync(loaderPlan)
                : "，processors 等待队列中依赖完成";
            MarkInstalledInstanceSelected(instanceName);
            RefreshTaskSnapshots();
            var skippedCount = existingSkippedCount + queuedSkippedCount;
            var skippedText = skippedCount == 0
                ? ""
                : $"，已跳过 {existingSkippedCount} 个已存在文件、{queuedSkippedCount} 个队列中任务";
            StatusMessage = $"{snapshot.Message}{skippedText}，已安装 {SelectedLoaderKind} {LoaderVersion.Trim()}，已设为当前版本{postDownloadProcessorMessage}";
        });
    }

    private Task<LoaderInstallPlan> CreateLoaderInstallPlanAsync(string instanceName, string instancePath)
    {
        var loaderVersion = LoaderVersion.Trim();
        var minecraftVersion = SelectedVersion?.Id ?? "";
        return SelectedLoaderKind.ToLowerInvariant() switch
        {
            "fabric" => (_fabricLoaderInstall ?? throw new InvalidOperationException("Fabric 安装服务未初始化。"))
                .CreateInstallPlanAsync(MinecraftRootPath, instanceName, instancePath, minecraftVersion, loaderVersion),
            "quilt" => (_quiltLoaderInstall ?? throw new InvalidOperationException("Quilt 安装服务未初始化。"))
                .CreateInstallPlanAsync(MinecraftRootPath, instanceName, instancePath, minecraftVersion, loaderVersion),
            "forge" => (_forgeLoaderInstall ?? throw new InvalidOperationException("Forge 安装服务未初始化。"))
                .CreateInstallPlanAsync(MinecraftRootPath, instanceName, instancePath, minecraftVersion, loaderVersion),
            "neoforge" => (_neoForgeLoaderInstall ?? throw new InvalidOperationException("NeoForge 安装服务未初始化。"))
                .CreateInstallPlanAsync(MinecraftRootPath, instanceName, instancePath, minecraftVersion, loaderVersion),
            _ => throw new InvalidOperationException("未知加载器类型：" + SelectedLoaderKind)
        };
    }

    private async Task<string> RunLoaderProcessorsIfPossibleAsync(LoaderInstallPlan plan)
    {
        if (plan.Processors.Count == 0)
        {
            return "";
        }

        var javaPath = _settings.Get(AppSettingKeys.LaunchJavaPath, "");
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
        {
            return "，processors 等待 Java 路径";
        }

        var result = await _processorRunner.RunAsync(MinecraftRootPath, javaPath, plan.Processors);
        if (result.Success)
        {
            return $"，processors 已执行 {result.ExecutedProcessors.Count} 个";
        }

        var issueCount = result.MissingInputs.Count + result.MissingOutputs.Count + result.FailedProcessors.Count;
        return $"，processors 未完成 {issueCount} 项";
    }

    private string InstanceVersionSafeName => string.IsNullOrWhiteSpace(InstanceName) ? SelectedVersion?.Id ?? "Unknown" : InstanceName.Trim();

    private string GetLoaderInstanceName()
    {
        var minecraftVersion = SelectedVersion?.Id ?? "Unknown";
        var loaderName = SelectedLoaderKind.Trim();
        var loaderVersion = LoaderVersion.Trim();
        if (!string.IsNullOrWhiteSpace(InstanceName)
            && !string.Equals(InstanceName.Trim(), minecraftVersion, StringComparison.OrdinalIgnoreCase))
        {
            return InstanceName.Trim();
        }

        return string.IsNullOrWhiteSpace(loaderVersion)
            ? $"{minecraftVersion}-{loaderName}"
            : $"{minecraftVersion}-{loaderName}-{loaderVersion}";
    }

    private void MarkInstalledInstanceSelected(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return;
        }

        var selectedName = instanceName.Trim();
        _settings.Set(AppSettingKeys.SelectedInstanceName, selectedName);
        _selections.WriteSelectedInstanceName(MinecraftRootPath, selectedName);
        _selections.ClearInstanceCache(MinecraftRootPath);
        UpdateResourceInstallTargetLabel();
        _logger.Info("下载安装完成，已切换当前版本并清空实例缓存：" + selectedName);
    }

    private async Task<string> ResolveResourceInstallRootAsync()
    {
        if (SelectedResourceProject?.Type == CommunityResourceType.ModPack)
        {
            ResourceInstallTarget = "当前 Minecraft 文件夹：" + MinecraftRootPath;
            return MinecraftRootPath;
        }

        var selectedName = GetSelectedInstanceNameForRoot();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            ResourceInstallTarget = "未选择启动实例，回退到 Minecraft 文件夹：" + MinecraftRootPath;
            return MinecraftRootPath;
        }

        var instances = await _minecraftDiscovery.ScanAsync(MinecraftRootPath);
        var instance = instances.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (instance is null)
        {
            ResourceInstallTarget = "未找到当前启动实例 " + selectedName + "，回退到 Minecraft 文件夹：" + MinecraftRootPath;
            return MinecraftRootPath;
        }

        var directory = _gameDirectories.Resolve(instance);
        ResourceInstallTarget = (directory.IsIsolated ? "版本隔离目录：" : "公共游戏目录：") + directory.Path;
        return directory.Path;
    }

    private string GetSelectedInstanceNameForRoot()
    {
        var selectedName = _selections.ReadSelectedInstanceName(MinecraftRootPath);
        return string.IsNullOrWhiteSpace(selectedName)
            ? _settings.Get(AppSettingKeys.SelectedInstanceName, "")
            : selectedName;
    }

    private void UpdateResourceInstallTargetLabel()
    {
        if (SelectedResourceProject?.Type == CommunityResourceType.ModPack
            || SelectedSection.Section == DownloadSection.ModPack)
        {
            ResourceInstallTarget = "整合包文件会下载到当前 Minecraft 文件夹下的 PCL\\Downloads\\ModPacks；安装请使用本地整合包入口";
            return;
        }

        var selectedName = GetSelectedInstanceNameForRoot();
        ResourceInstallTarget = string.IsNullOrWhiteSpace(selectedName)
            ? "资源将下载到当前 Minecraft 文件夹的对应目录（尚未选择启动实例）"
            : "资源将下载到当前启动实例的游戏目录：" + selectedName;
    }
}
