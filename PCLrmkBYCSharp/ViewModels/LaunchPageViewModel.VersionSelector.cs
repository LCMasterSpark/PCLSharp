using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;


public sealed partial class LaunchPageViewModel
{
    public void PrepareSelectedVersionForManagement()
    {
        _settings.Set(AppSettingKeys.InstanceManageSelectedName, SelectedInstance?.Name ?? "");
    }

    public void DismissVersionSelector()
    {
        IsVersionSelectorOpen = false;
    }

    partial void OnIsBusyChanged(bool value)
    {
        CancelBusyCommand?.NotifyCanExecuteChanged();
        LoginMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        RefreshMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        SwitchMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        DeleteMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        LoginServerAccountCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanStartMicrosoftLogin));
        OnPropertyChanged(nameof(CanRefreshMicrosoftLogin));
        OnPropertyChanged(nameof(MicrosoftLoginUnavailableReason));
        OnPropertyChanged(nameof(MicrosoftRefreshUnavailableReason));
    }

    partial void OnSelectedMicrosoftAccountChanged(MicrosoftAccountCacheEntry? value)
    {
        SwitchMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        DeleteMicrosoftAccountCommand?.NotifyCanExecuteChanged();
    }

    partial void OnSelectedJavaChanged(JavaEntry? value)
    {
        if (_isSyncingJavaOptionSelection)
        {
            OnPropertyChanged(nameof(SelectedJavaSummary));
            return;
        }

        if (!_isSyncingJavaOptionSelection)
        {
            _isSyncingJavaOptionSelection = true;
            try
            {
                SelectedJavaOption = FindJavaOption(value);
            }
            finally
            {
                _isSyncingJavaOptionSelection = false;
            }
        }

        if (_isRestoringJavaSelection)
        {
            OnPropertyChanged(nameof(SelectedJavaSummary));
            return;
        }

        _settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, JavaEntry.ToPclSettingJson(value));
        OnPropertyChanged(nameof(SelectedJavaSummary));
        _ = SaveSettingsAsync();
    }

    partial void OnSelectedJavaOptionChanged(JavaEntryOption? value)
    {
        if (_isSyncingJavaOptionSelection)
        {
            return;
        }

        _isSyncingJavaOptionSelection = true;
        try
        {
            SelectedJava = value?.Entry;
        }
        finally
        {
            _isSyncingJavaOptionSelection = false;
        }

        if (!_isRestoringJavaSelection)
        {
            _settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, JavaEntry.ToPclSettingJson(value?.Entry));
            OnPropertyChanged(nameof(SelectedJavaSummary));
            _ = SaveSettingsAsync();
        }
    }

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

            _isInitialized = false;
            _ = InitializeAsync();
        }
    }

    partial void OnVersionSearchTextChanged(string value)
    {
        RefreshVersionSelectorRows();
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
        RefreshVersionSelectorRows();
    }

    partial void OnServerIpChanged(string value)
    {
        var normalized = NormalizeServerIp(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            ServerIp = normalized;
        }
    }

    partial void OnShowHiddenVersionsChanged(bool value)
    {
        RefreshVersionSelectorRows();
        StatusMessage = value ? "正在查看隐藏版本" : "正在查看可用版本";
    }

    partial void OnLaunchDiagnosticsChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchDiagnostics));
    }

    private void OpenVersionSelector()
    {
        if (SelectedInstance?.IsHidden == true && !ShowHiddenVersions)
        {
            ShowHiddenVersions = true;
        }

        if (SelectedInstance is not null && !MatchesVersionSearch(SelectedInstance, VersionSearchText.Trim()))
        {
            VersionSearchText = "";
        }

        RefreshVersionSelectorRows();
        IsVersionSelectorOpen = true;
        StatusMessage = Instances.Count == 0 ? "暂无可选版本，请先刷新版本列表" : "请选择要启动的版本";
    }

    private void CloseVersionSelector()
    {
        IsVersionSelectorOpen = false;
    }

    private void SelectVersion(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        if (instance.HasError)
        {
            StatusMessage = string.IsNullOrWhiteSpace(instance.ErrorMessage)
                ? $"版本 {instance.Name} 状态异常，无法设为启动版本"
                : $"版本 {instance.Name} 状态异常，无法设为启动版本：{instance.ErrorMessage}";
            _logger.Warn(StatusMessage);
            return;
        }

        IsVersionSelectorOpen = false;
        SelectedInstance = instance;
        UpdateVersionSelectorRowRoles();
        StatusMessage = $"{instance.Name} 已设为启动版本";
    }

    private async Task ToggleVersionStarAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        var wasOpen = IsVersionSelectorOpen;
        var next = !instance.IsStar;
        _instanceManagement.SetStar(instance, next);
        await RefreshInstancesCoreAsync();
        IsVersionSelectorOpen = wasOpen;
        StatusMessage = next
            ? $"{instance.Name} 已加入收藏夹"
            : $"{instance.Name} 已取消收藏";
        _logger.Info(StatusMessage);
    }

    private async Task ToggleVersionHiddenAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        var wasOpen = IsVersionSelectorOpen;
        var hide = !instance.IsHidden;
        var isCurrentLaunchVersion = string.Equals(SelectedInstance?.Name, instance.Name, StringComparison.OrdinalIgnoreCase);
        _instanceManagement.SetDisplayType(instance, hide ? MinecraftInstanceDisplayType.Hidden : MinecraftInstanceDisplayType.Auto);
        if (isCurrentLaunchVersion)
        {
            ShowHiddenVersions = hide;
        }

        await RefreshInstancesCoreAsync();
        IsVersionSelectorOpen = wasOpen;
        StatusMessage = hide
            ? $"{instance.Name} 已隐藏"
            : $"{instance.Name} 已取消隐藏";
        _logger.Info(StatusMessage);
    }

    private async Task DeleteVersionAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        if (!_prompts.Confirm("版本删除确认", $"你确定要删除版本 {instance.Name} 吗？"))
        {
            StatusMessage = "已取消删除版本";
            return;
        }

        var wasOpen = IsVersionSelectorOpen;
        var deletedName = instance.Name;
        var wasLaunchVersion = string.Equals(SelectedInstance?.Name, deletedName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_settings.Get(AppSettingKeys.SelectedInstanceName, ""), deletedName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_selections.ReadSelectedInstanceName(MinecraftRootPath), deletedName, StringComparison.OrdinalIgnoreCase);
        _instanceManagement.DeleteInstance(instance);
        if (string.Equals(_settings.Get(AppSettingKeys.SelectedInstanceName, ""), deletedName, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Set(AppSettingKeys.SelectedInstanceName, "");
            _selections.WriteSelectedInstanceName(MinecraftRootPath, "");
        }

        if (string.Equals(_settings.Get(AppSettingKeys.InstanceManageSelectedName, ""), deletedName, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Set(AppSettingKeys.InstanceManageSelectedName, "");
        }

        await RefreshInstancesCoreAsync();
        IsVersionSelectorOpen = wasOpen;
        StatusMessage = wasLaunchVersion
            ? SelectedInstance is null
                ? $"版本 {deletedName} 已删除，当前没有可用启动版本"
                : $"版本 {deletedName} 已删除，已切换到 {SelectedInstance.Name}"
            : $"版本 {deletedName} 已删除";
        _logger.Info(StatusMessage);
        await SaveSettingsAsync();
    }

    private void OpenVersionFolder(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            _folders.OpenFolder(instance.VersionPath);
            StatusMessage = $"已打开版本文件夹：{instance.Name}";
            _logger.Info(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开版本文件夹失败：{ex.Message}";
            _logger.Error(ex, "打开版本文件夹失败");
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        if (SyncMinecraftRootPathFromSettings())
        {
            _isInitialized = false;
        }

        SyncVersionSortModeFromSettings();
        if (IsBusy)
        {
            return;
        }

        if (!_isInitialized)
        {
            await InitializeAsync();
        }
        else
        {
            await RefreshInstancesAsync();
        }

        await ConsumeFileCompletionFeedbackAsync();
    }
}
