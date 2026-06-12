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
    public async Task RefreshCurrentSectionAsync()
    {
        if (SelectedSection.Section == DownloadSection.Install)
        {
            await RefreshVersionsAsync();
            return;
        }

        RefreshTaskSnapshots();
        StatusMessage = SelectedSection.Section == DownloadSection.Manager
            ? $"下载管理已刷新：{DownloadTaskCount} 个任务"
            : $"{SelectedSection.Title} 可以开始搜索";
    }

    private void ApplyPendingPreset()
    {
        var sectionValue = _settings.Get(AppSettingKeys.DownloadPresetResourceSection, -1);
        if (sectionValue < 0)
        {
            UpdateResourceInstallTargetLabel();
            return;
        }

        if (Enum.IsDefined(typeof(DownloadSection), sectionValue))
        {
            var section = (DownloadSection)sectionValue;
            SelectedSection = Sections.FirstOrDefault(item => item.Section == section) ?? SelectedSection;
        }

        ResourceSearchText = _settings.Get(AppSettingKeys.DownloadPresetSearchText, "");
        ResourceGameVersion = _settings.Get(AppSettingKeys.DownloadPresetGameVersion, "");
        ResourceLoader = _settings.Get(AppSettingKeys.DownloadPresetLoader, "");
        ResourceProjects.Clear();
        ResourceVersions.Clear();
        SelectedResourceProject = null;
        SelectedResourceVersion = null;
        SelectedResourceFile = null;
        OnPropertyChanged(nameof(ResourceResultCount));
        OnPropertyChanged(nameof(ResourceVersionCount));
        OnPropertyChanged(nameof(SelectedResourceDetail));
        OnPropertyChanged(nameof(SelectedResourceFileSummary));
        _settings.Set(AppSettingKeys.DownloadPresetResourceSection, -1);
        _settings.Set(AppSettingKeys.DownloadPresetSearchText, "");
        _settings.Set(AppSettingKeys.DownloadPresetGameVersion, "");
        _settings.Set(AppSettingKeys.DownloadPresetLoader, "");
        UpdateResourceInstallTargetLabel();
        StatusMessage = "已套用当前实例的资源搜索条件";
        _ = _settings.SaveAsync();
    }

    public async Task SearchResourcesAsync()
    {
        if (!IsResourceSection)
        {
            StatusMessage = "当前分类不需要社区资源搜索";
            return;
        }

        await RunBusyAsync("正在搜索 " + SelectedSection.Title + "...", async () =>
        {
            var result = await _communityResourceSearch.SearchAsync(new CommunityResourceSearchQuery(
                ToResourceType(SelectedSection.Section),
                ResourceSearchText,
                ResourceGameVersion,
                ResourceLoader));
            _lastResourceProjects.Clear();
            _lastResourceProjects.AddRange(result.Projects);
            ResourceVersions.Clear();
            SelectedResourceVersion = null;
            SelectedResourceFile = null;
            ApplyResourceSourceFilter();
            OnPropertyChanged(nameof(ResourceResultCount));
            OnPropertyChanged(nameof(ResourceVersionCount));
            StatusMessage = $"从 {result.SourceMessage} 搜索到 {ResourceProjects.Count} 个结果，共 {result.TotalHits} 个匹配项";
        });

        AutoLoadResourceVersionsForSelection();
    }

    private void ApplyResourceSourceFilter()
    {
        var currentId = SelectedResourceProject?.Id;
        var currentPlatform = SelectedResourceProject?.Platform;
        var filtered = SelectedResourceSource switch
        {
            "Modrinth" => _lastResourceProjects.Where(project => project.Platform == CommunityResourcePlatform.Modrinth),
            "CurseForge" => _lastResourceProjects.Where(project => project.Platform == CommunityResourcePlatform.CurseForge),
            _ => _lastResourceProjects
        };

        ResourceProjects.Clear();
        foreach (var project in filtered)
        {
            ResourceProjects.Add(project);
        }

        SelectedResourceProject = ResourceProjects.FirstOrDefault(project =>
                string.Equals(project.Id, currentId, StringComparison.OrdinalIgnoreCase) && project.Platform == currentPlatform)
            ?? ResourceProjects.FirstOrDefault();
        OnPropertyChanged(nameof(ResourceResultCount));
    }

    public async Task LoadResourceVersionsAsync()
    {
        if (SelectedResourceProject is null)
        {
            StatusMessage = "请先选择一个资源";
            return;
        }

        await RunBusyAsync("正在获取资源版本...", async () =>
        {
            var versions = await _communityResourceVersions.GetVersionsAsync(
                SelectedResourceProject,
                ResourceGameVersion,
                ResourceLoader);
            ResourceVersions.Clear();
            foreach (var version in versions)
            {
                ResourceVersions.Add(version);
            }

            SelectedResourceVersion = ResourceVersions.FirstOrDefault();
            OnPropertyChanged(nameof(ResourceVersionCount));
            StatusMessage = $"已获取 {ResourceVersions.Count} 个可下载版本";
        });
    }

    private void AutoLoadResourceVersionsForSelection()
    {
        if (!IsResourceSection || SelectedResourceProject is null || IsBusy || _isAutoLoadingResourceVersions)
        {
            return;
        }

        _ = LoadResourceVersionsAutomaticallyAsync(SelectedResourceProject);
    }

    private async Task LoadResourceVersionsAutomaticallyAsync(CommunityResourceProject project)
    {
        _isAutoLoadingResourceVersions = true;
        try
        {
            StatusMessage = "正在自动获取资源版本...";
            var versions = await _communityResourceVersions.GetVersionsAsync(
                project,
                ResourceGameVersion,
                ResourceLoader);
            if (SelectedResourceProject != project || SelectedResourceVersion is not null)
            {
                return;
            }

            ResourceVersions.Clear();
            foreach (var version in versions)
            {
                ResourceVersions.Add(version);
            }

            SelectedResourceVersion = ResourceVersions.FirstOrDefault();
            OnPropertyChanged(nameof(ResourceVersionCount));
            NotifySelectedResourceDownloadStateChanged();
            StatusMessage = $"已自动获取 {ResourceVersionCount} 个 {project.Name} 版本";
        }
        catch (Exception ex)
        {
            StatusMessage = "自动获取资源版本失败：" + ex.Message;
            _logger.Error(ex, "自动获取资源版本失败");
        }
        finally
        {
            _isAutoLoadingResourceVersions = false;
        }
    }

    public async Task DownloadSelectedResourceFileAsync()
    {
        if (SelectedResourceProject is null || SelectedResourceVersion is null || SelectedResourceFile is null)
        {
            StatusMessage = "请先选择资源版本和文件";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedResourceFile.Url))
        {
            StatusMessage = "所选文件缺少下载地址，无法创建下载任务";
            return;
        }

        await RunBusyAsync("正在创建资源下载任务...", async () =>
        {
            _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
            var targetRoot = await ResolveResourceInstallRootAsync();
            var files = await _communityResourceVersions.CreateDownloadFilesWithDependenciesAsync(
                SelectedResourceProject,
                SelectedResourceVersion,
                SelectedResourceFile,
                targetRoot,
                ResourceGameVersion,
                ResourceLoader);
            var requestedFileCount = files.Count;
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
                RefreshTaskSnapshots();
                StatusMessage = queuedSkippedCount > 0
                    ? "所选文件、必需依赖或队列中任务已覆盖，已跳过重复下载"
                    : "所选文件和必需依赖已存在，已跳过重复下载";
                return;
            }

            var snapshot = await _downloadManager.DownloadAsync(SelectedResourceProject.Name + " 下载", files);
            RefreshTaskSnapshots();
            var skippedCount = requestedFileCount - files.Count;
            var skippedParts = new List<string>();
            if (existingSkippedCount > 0)
            {
                skippedParts.Add($"{existingSkippedCount} 个已存在文件");
            }

            if (queuedSkippedCount > 0)
            {
                skippedParts.Add($"{queuedSkippedCount} 个队列中任务");
            }

            var skippedText = skippedCount == 0 ? "" : "，已跳过 " + string.Join("、", skippedParts);
            StatusMessage = files.Count > 1
                ? $"{snapshot.Message}，已包含 {files.Count - 1} 个必需依赖{skippedText}"
                : snapshot.Message + skippedText;
        });
    }

    private static bool IsExistingDownloadSatisfied(DownloadFile file)
    {
        if (!file.Check.CanUseExistingFile || string.IsNullOrWhiteSpace(file.LocalPath) || !File.Exists(file.LocalPath))
        {
            return false;
        }

        var length = new FileInfo(file.LocalPath).Length;
        if (file.Check.ActualSize > 0)
        {
            return length == file.Check.ActualSize;
        }

        if (file.Check.MinSize > 0)
        {
            return length >= file.Check.MinSize;
        }

        return true;
    }

    private bool IsDownloadAlreadyQueued(DownloadFile file)
    {
        if (string.IsNullOrWhiteSpace(file.LocalPath))
        {
            return false;
        }

        return _downloadManager.Tasks.Any(task =>
            task.State is DownloadTaskState.Waiting or DownloadTaskState.Running
            && IsPathCoveredByTask(task, file.LocalPath));
    }

    private static bool IsPathCoveredByTask(DownloadTaskSnapshot task, string localPath)
    {
        if (task.LocalPaths.Any(path => string.Equals(path, localPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(task.PrimaryLocalPath, localPath, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenSelectedResourceProject()
    {
        if (SelectedResourceProject is null || string.IsNullOrWhiteSpace(SelectedResourceProject.WebsiteUrl))
        {
            StatusMessage = "请先选择一个资源";
            return;
        }

        try
        {
            _urls.OpenUrl(SelectedResourceProject.WebsiteUrl);
            StatusMessage = "已打开资源页面：" + SelectedResourceProject.Name;
        }
        catch (Exception ex)
        {
            StatusMessage = "打开资源页面失败：" + ex.Message;
            _logger.Error(ex, "打开资源页面失败");
        }
    }
}
