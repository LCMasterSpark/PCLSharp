using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;


public sealed partial class InstancePageViewModel
{
    private async Task ToggleSelectedLocalModEnabledAsync()
    {
        if (SelectedLocalMod is null)
        {
            StatusMessage = "请先选择一个 Mod";
            return;
        }

        try
        {
            var next = !SelectedLocalMod.IsEnabled;
            _localMods.SetEnabled(SelectedLocalMod.Mod, next);
            var name = SelectedLocalMod.Title;
            await RefreshLocalModsAsync();
            StatusMessage = $"已{(next ? "启用" : "禁用")} Mod：{name}";
        }
        catch (Exception ex)
        {
            StatusMessage = "切换 Mod 状态失败：" + ex.Message;
            _logger.Error(ex, "切换 Mod 状态失败");
        }
    }

    private async Task InstallLocalModsAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个可以安装 Mod 的版本";
            return;
        }

        var files = _fileDialogs.PickModFiles(LocalModsDirectory);
        if (files.Count == 0)
        {
            StatusMessage = "未选择要安装的 Mod";
            return;
        }

        try
        {
            var installed = _localMods.Install(LocalModsDirectory, files);
            await RefreshLocalModsAsync();
            StatusMessage = installed.Count == 1
                ? "已安装 " + installed[0].EnabledFileName
                : $"已安装 {installed.Count} 个 Mod";
        }
        catch (Exception ex)
        {
            StatusMessage = "安装 Mod 失败：" + ex.Message;
            _logger.Error(ex, "安装 Mod 失败");
        }
    }

    private async Task CheckLocalModUpdatesAsync()
    {
        if (DisableModUpdate)
        {
            StatusMessage = "当前版本已禁止更新 Mod";
            return;
        }

        if (_localModUpdates is null)
        {
            StatusMessage = "本地 Mod 更新检查服务尚未初始化";
            return;
        }

        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        if (_allLocalMods.Count == 0)
        {
            await RefreshLocalModsAsync();
        }

        if (_allLocalMods.Count == 0)
        {
            StatusMessage = "当前版本没有可检查的 Mod";
            return;
        }

        IsCheckingLocalModUpdates = true;
        try
        {
            StatusMessage = "正在检查本地 Mod 更新...";
            var updates = await _localModUpdates.CheckModrinthUpdatesAsync(
                _allLocalMods,
                SelectedInstance.DisplayVersion,
                GetPreferredLoader(SelectedInstance));
            _localModUpdateInfos.Clear();
            foreach (var pair in updates)
            {
                _localModUpdateInfos[pair.Key] = pair.Value;
            }

            RefreshLocalModRows();
            StatusMessage = UpdateLocalModCount == 0
                ? "本地 Mod 更新检查完成，未发现可更新项"
                : $"本地 Mod 更新检查完成，发现 {UpdateLocalModCount} 个可更新项";
        }
        catch (Exception ex)
        {
            StatusMessage = "检查 Mod 更新失败：" + ex.Message;
            _logger.Error(ex, "检查 Mod 更新失败");
        }
        finally
        {
            IsCheckingLocalModUpdates = false;
        }
    }

    private async Task DownloadModsForSelectedInstanceAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个可以安装 Mod 的版本";
            return;
        }

        _settings.Set(AppSettingKeys.DownloadPresetResourceSection, (int)DownloadSection.Mod);
        _settings.Set(AppSettingKeys.DownloadPresetSearchText, LocalModSearchText);
        _settings.Set(AppSettingKeys.DownloadPresetGameVersion, SelectedInstance.DisplayVersion);
        _settings.Set(AppSettingKeys.DownloadPresetLoader, GetPreferredLoader(SelectedInstance));
        _settings.Set(AppSettingKeys.SelectedInstanceName, SelectedInstance.Name);
        _settings.Set(AppSettingKeys.LastRoute, PageRoute.Download);
        _selections.WriteSelectedInstanceName(MinecraftRootPath, SelectedInstance.Name);
        await _settings.SaveAsync();
        StatusMessage = $"已为 {SelectedInstance.Name} 准备 Mod 下载筛选，请切换到下载页";
    }

    private async Task UpdateSelectedLocalModsAsync()
    {
        var selected = GetSelectedLocalMods()
            .Where(HasLocalModUpdate)
            .ToList();
        await UpdateLocalModsAsync(selected, "选中的 Mod");
    }

    private async Task UpdateAllLocalModsAsync()
    {
        var updateTargets = _allLocalMods
            .Where(HasLocalModUpdate)
            .ToList();
        await UpdateLocalModsAsync(updateTargets, "全部可更新 Mod");
    }

    private async Task UpdateLocalModsAsync(IReadOnlyList<LocalModFile> mods, string scopeName)
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        if (mods.Count == 0)
        {
            StatusMessage = "没有可更新的 Mod";
            return;
        }

        var updates = mods
            .Select(mod => (Mod: mod, Info: GetLocalModUpdate(mod)))
            .Where(pair => pair.Info?.LatestFile is not null)
            .Select(pair => new LocalModUpdateDownload(pair.Mod, pair.Info!, BuildLocalModUpdateDownloadFile(pair.Mod, pair.Info!)))
            .ToList();
        if (updates.Count == 0)
        {
            StatusMessage = "没有可下载的 Mod 更新";
            return;
        }

        try
        {
            StatusMessage = $"正在更新{scopeName}...";
            var taskName = $"{SelectedInstance.Name} Mod 更新";
            var snapshot = await _downloadManager.DownloadAsync(taskName, updates.Select(update => update.Download).ToList());
            if (snapshot.State != DownloadTaskState.Succeeded)
            {
                StatusMessage = "Mod 更新失败：" + snapshot.Message;
                return;
            }

            foreach (var update in updates)
            {
                DeleteReplacedLocalMod(update.Mod.FilePath, update.Download.LocalPath);
            }

            _localModUpdateInfos.Clear();
            await RefreshLocalModsAsync();
            StatusMessage = $"已更新 {updates.Count} 个 Mod";
        }
        catch (Exception ex)
        {
            StatusMessage = "更新 Mod 失败：" + ex.Message;
            _logger.Error(ex, "更新 Mod 失败");
        }
    }

    private bool CanCheckLocalModUpdates()
    {
        return SelectedInstance is not null && LocalMods.Count > 0 && !DisableModUpdate;
    }

    private bool CanCompleteSelectedInstanceFiles()
    {
        return SelectedInstance is not null && !DisableFileCheck && !IsCompletingFiles;
    }

    private void ToggleLocalModSelection(LocalModListRow? row)
    {
        if (row is null)
        {
            return;
        }

        var key = row.Mod.EnabledFileName;
        if (!_selectedLocalModKeys.Add(key))
        {
            _selectedLocalModKeys.Remove(key);
        }

        RefreshLocalModRows();
    }

    private void SelectAllLocalMods()
    {
        _selectedLocalModKeys.Clear();
        foreach (var row in LocalMods)
        {
            _selectedLocalModKeys.Add(row.Mod.EnabledFileName);
        }

        RefreshLocalModRows();
    }

    private void ClearSelectedLocalMods()
    {
        _selectedLocalModKeys.Clear();
        RefreshLocalModRows();
    }

    private async Task EnableSelectedLocalModsAsync()
    {
        await SetSelectedLocalModsEnabledAsync(true);
    }

    private async Task DisableSelectedLocalModsAsync()
    {
        await SetSelectedLocalModsEnabledAsync(false);
    }

    private async Task SetSelectedLocalModsEnabledAsync(bool enabled)
    {
        var selected = GetSelectedLocalMods().Where(mod => mod.IsEnabled != enabled).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = enabled ? "没有可启用的已选 Mod" : "没有可禁用的已选 Mod";
            return;
        }

        try
        {
            foreach (var mod in selected)
            {
                _localMods.SetEnabled(mod, enabled);
            }

            _selectedLocalModKeys.Clear();
            await RefreshLocalModsAsync();
            StatusMessage = $"已{(enabled ? "启用" : "禁用")} {selected.Count} 个 Mod";
        }
        catch (Exception ex)
        {
            StatusMessage = "批量切换 Mod 状态失败：" + ex.Message;
            _logger.Error(ex, "批量切换 Mod 状态失败");
        }
    }

    private async Task DeleteSelectedLocalModsAsync()
    {
        var selected = GetSelectedLocalMods().ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "请先选择要删除的 Mod";
            return;
        }

        if (ShouldConfirmDangerousActions()
            && !_prompts.Confirm("删除 Mod", $"你确定要删除选中的 {selected.Count} 个 Mod 吗？"))
        {
            StatusMessage = "已取消删除 Mod";
            return;
        }

        try
        {
            foreach (var mod in selected)
            {
                _localMods.Delete(mod);
            }

            _selectedLocalModKeys.Clear();
            await RefreshLocalModsAsync();
            StatusMessage = $"已删除 {selected.Count} 个 Mod";
        }
        catch (Exception ex)
        {
            StatusMessage = "批量删除 Mod 失败：" + ex.Message;
            _logger.Error(ex, "批量删除 Mod 失败");
        }
    }

    private async Task DeleteSelectedLocalModAsync()
    {
        if (SelectedLocalMod is null)
        {
            StatusMessage = "请先选择一个 Mod";
            return;
        }

        if (ShouldConfirmDangerousActions()
            && !_prompts.Confirm("删除 Mod", $"你确定要删除 Mod {SelectedLocalMod.Title} 吗？"))
        {
            StatusMessage = "已取消删除 Mod";
            return;
        }

        try
        {
            var name = SelectedLocalMod.Title;
            _localMods.Delete(SelectedLocalMod.Mod);
            await RefreshLocalModsAsync();
            StatusMessage = "已删除 Mod：" + name;
        }
        catch (Exception ex)
        {
            StatusMessage = "删除 Mod 失败：" + ex.Message;
            _logger.Error(ex, "删除 Mod 失败");
        }
    }
}
