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
    partial void OnMinecraftRootPathChanged(string value)
    {
        _settings.Set(AppSettingKeys.MinecraftRootPath, value);
        _settings.Set(AppSettingKeys.LaunchFolderSelect, value);
        if (!_isChangingRootPathFromSelection)
        {
            RefreshMinecraftRootFolders();
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
            _isChangingRootPathFromSelection = true;
            try
            {
                MinecraftRootPath = value.Path;
            }
            finally
            {
                _isChangingRootPathFromSelection = false;
            }

            _ = RefreshAsync();
        }
    }

    partial void OnSelectedInstanceChanged(MinecraftInstance? value)
    {
        _settings.Set(AppSettingKeys.InstanceManageSelectedName, value?.Name ?? "");
        UpdateInstanceRowRoles();
        SyncSelectedInstanceRow(value?.Name);
        LoadLaunchSettings(value?.Name);
        RaiseSelectedInstanceFeedbackChanged();
        ResetExportMetadata(value);
        UseSelectedInstanceForLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedSavesFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedModsFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedResourcePacksFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedShaderPacksFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedScreenshotsFolderCommand.NotifyCanExecuteChanged();
        RenameSelectedInstanceCommand.NotifyCanExecuteChanged();
        CloneSelectedInstanceCommand.NotifyCanExecuteChanged();
        ExportSelectedInstanceScriptCommand.NotifyCanExecuteChanged();
        ExportSelectedInstanceModpackCommand.NotifyCanExecuteChanged();
        ToggleSelectedInstanceStarCommand.NotifyCanExecuteChanged();
        ToggleSelectedInstanceHiddenCommand.NotifyCanExecuteChanged();
        DeleteSelectedInstanceCommand.NotifyCanExecuteChanged();
        RefreshLocalModsCommand.NotifyCanExecuteChanged();
        CheckLocalModUpdatesCommand.NotifyCanExecuteChanged();
        InstallLocalModsCommand.NotifyCanExecuteChanged();
        DownloadModsForSelectedInstanceCommand.NotifyCanExecuteChanged();
        CompleteSelectedInstanceFilesCommand.NotifyCanExecuteChanged();
        ResetInstanceLaunchSettingsCommand.NotifyCanExecuteChanged();
        _ = RefreshLocalModsAsync();
        _ = SaveSettingsAsync();
    }

    partial void OnSelectedInstanceRowChanged(InstanceListRow? value)
    {
        if (_isSyncingInstanceRowSelection || value?.Instance is null)
        {
            return;
        }

        SelectInstance(value.Instance);
    }

    partial void OnShowHiddenInstancesChanged(bool value)
    {
        if (_isRestoringSelection)
        {
            return;
        }

        RefreshVisibleInstances();
        RestoreSelection();
        StatusMessage = value ? "正在显示隐藏版本" : "已隐藏被标记的版本";
    }

    partial void OnInstanceSearchTextChanged(string value)
    {
        RefreshVisibleInstances();
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "已清空版本搜索"
            : "正在筛选版本：" + value.Trim();
    }

    partial void OnVersionSortModeChanged(int value)
    {
        var normalized = NormalizeVersionSortMode(value);
        if (normalized != value)
        {
            VersionSortMode = normalized;
            return;
        }

        _settings.Set(AppSettingKeys.VersionSortMode, normalized);
        RefreshVisibleInstances();
        StatusMessage = VersionSortOptions.FirstOrDefault(option => option.Value == normalized)?.DisplayName ?? "按发布时间";
    }

    partial void OnVersionIsolationEnabledChanged(bool value)
    {
        UpdateGameDirectoryPath();
        if (_isLoadingLaunchSettings || SelectedInstance is null)
        {
            return;
        }

        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentIndieV2, value);
        RaiseSelectedInstanceFeedbackChanged();
        _ = RefreshLocalModsAsync();
        _ = SaveSettingsAsync();
    }

    partial void OnVersionServerLoginChanged(int value)
    {
        OnPropertyChanged(nameof(IsVersionNideServerLogin));
        OnPropertyChanged(nameof(IsVersionAuthServerLogin));
    }

    partial void OnServerIpChanged(string value)
    {
        var normalized = NormalizeServerIp(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            ServerIp = normalized;
        }
    }

    partial void OnDisableModUpdateChanged(bool value)
    {
        CheckLocalModUpdatesCommand.NotifyCanExecuteChanged();
    }

    partial void OnDisableFileCheckChanged(bool value)
    {
        CompleteSelectedInstanceFilesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCompletingFilesChanged(bool value)
    {
        CompleteSelectedInstanceFilesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLocalModChanged(LocalModListRow? value)
    {
        ToggleSelectedLocalModEnabledCommand.NotifyCanExecuteChanged();
        DeleteSelectedLocalModCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedLocalModActionText));
    }

    partial void OnLocalModSearchTextChanged(string value)
    {
        RefreshLocalModRows();
    }

    partial void OnLocalModFilterChanged(int value)
    {
        RefreshLocalModRows();
    }

    partial void OnSelectedInstanceDetailSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsInstanceOverviewSectionSelected));
        OnPropertyChanged(nameof(IsInstanceModSectionSelected));
        OnPropertyChanged(nameof(IsInstanceLaunchSettingsSectionSelected));
    }

    public override async Task OnNavigatedToAsync()
    {
        SyncMinecraftRootPathFromSettings();
        SyncVersionSortModeFromSettings();
        if (IsScanning || IsCompletingFiles)
        {
            return;
        }

        await RefreshAsync();
    }





    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存实例设置失败");
        }
    }
}
