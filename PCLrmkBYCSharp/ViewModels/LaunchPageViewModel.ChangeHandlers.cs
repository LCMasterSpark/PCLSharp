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
    partial void OnSelectedLoginTypeChanged(LoginType value)
    {
        _settings.Set(AppSettingKeys.LoginType, value);
        LoginUserName = GetLoginUserName(value);
        LoginPassword = GetLoginPassword(value);
        LoginServer = GetLoginServer(value);
        OnPropertyChanged(nameof(IsLegacyLogin));
        OnPropertyChanged(nameof(IsLegacySkinIdVisible));
        OnPropertyChanged(nameof(IsLegacySkinBrowseVisible));
        OnPropertyChanged(nameof(IsLegacySkinSlimVisible));
        OnPropertyChanged(nameof(LegacySkinSummary));
        OnPropertyChanged(nameof(IsMicrosoftLogin));
        OnPropertyChanged(nameof(IsServerLogin));
        OnPropertyChanged(nameof(SelectedLoginTypeDisplayName));
        OnPropertyChanged(nameof(LoginUserNameHistory));
        if (value == LoginType.Ms && !HasMicrosoftClientId)
        {
            IsMicrosoftClientIdEditorVisible = true;
        }
        OnPropertyChanged(nameof(MicrosoftAccountSummary));
        OnPropertyChanged(nameof(MicrosoftReadinessSummary));
        OnPropertyChanged(nameof(MicrosoftLoginActionText));
        OnPropertyChanged(nameof(MicrosoftClientIdHelp));
        OnPropertyChanged(nameof(CanStartMicrosoftLogin));
        OnPropertyChanged(nameof(CanRefreshMicrosoftLogin));
        OnPropertyChanged(nameof(MicrosoftLoginUnavailableReason));
        OnPropertyChanged(nameof(MicrosoftRefreshUnavailableReason));
        BrowseLegacySkinCommand.NotifyCanExecuteChanged();
        LoginMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        RefreshMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        SwitchMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        DeleteMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        LoginServerAccountCommand?.NotifyCanExecuteChanged();
        OnLoginAccountChanged();
        _ = SaveSettingsAsync();
    }

    partial void OnMicrosoftClientIdChanged(string value)
    {
        _settings.Set(AppSettingKeys.MicrosoftClientId, value.Trim());
        OnPropertyChanged(nameof(HasMicrosoftClientId));
        OnPropertyChanged(nameof(MicrosoftAccountSummary));
        OnPropertyChanged(nameof(MicrosoftReadinessSummary));
        OnPropertyChanged(nameof(MicrosoftClientIdHelp));
        OnPropertyChanged(nameof(CanStartMicrosoftLogin));
        OnPropertyChanged(nameof(CanRefreshMicrosoftLogin));
        OnPropertyChanged(nameof(MicrosoftLoginUnavailableReason));
        OnPropertyChanged(nameof(MicrosoftRefreshUnavailableReason));
        LoginMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        RefreshMicrosoftAccountCommand?.NotifyCanExecuteChanged();
        _ = SaveSettingsAsync();
    }

    partial void OnIsMicrosoftClientIdEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(MicrosoftClientIdEditorActionText));
    }

    partial void OnIsMicrosoftDeviceCodeActiveChanged(bool value)
    {
        OpenMicrosoftDeviceCodePageCommand.NotifyCanExecuteChanged();
        CopyMicrosoftDeviceCodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnLaunchSkinTypeChanged(int value)
    {
        _settings.Set(AppSettingKeys.LaunchSkinType, value);
        OnPropertyChanged(nameof(IsLegacySkinIdVisible));
        OnPropertyChanged(nameof(IsLegacySkinBrowseVisible));
        OnPropertyChanged(nameof(IsLegacySkinSlimVisible));
        OnPropertyChanged(nameof(LegacySkinIdLabel));
        OnPropertyChanged(nameof(LegacySkinSummary));
        BrowseLegacySkinCommand.NotifyCanExecuteChanged();
        _ = SaveSettingsAsync();
    }

    partial void OnLaunchSkinIdChanged(string value)
    {
        _settings.Set(AppSettingKeys.LaunchSkinID, value);
        OnPropertyChanged(nameof(LegacySkinSummary));
        _ = SaveSettingsAsync();
    }

    partial void OnLaunchSkinSlimChanged(bool value)
    {
        _settings.Set(AppSettingKeys.LaunchSkinSlim, value);
        _ = SaveSettingsAsync();
    }

    partial void OnSelectedInstanceChanged(MinecraftInstance? value)
    {
        _settings.Set(AppSettingKeys.SelectedInstanceName, value?.Name ?? "");
        _selections.WriteSelectedInstanceName(MinecraftRootPath, value?.Name ?? "");
        LoadLaunchSettings(value?.Name);
        SyncInstanceServerLoginCache(value?.Name);
        OnPropertyChanged(nameof(CurrentVersionTitle));
        OnPropertyChanged(nameof(CurrentVersionSubtitle));
        OnPropertyChanged(nameof(SelectedInstanceSummary));
        OnPropertyChanged(nameof(InstanceLaunchSettingsSummary));
        OnPropertyChanged(nameof(HasSelectedInstance));
        OnPropertyChanged(nameof(HasNoSelectedInstance));
        OnPropertyChanged(nameof(VersionSelectorSummary));
        if (IsVersionSelectorOpen)
        {
            UpdateVersionSelectorRowRoles();
        }
        _ = SaveSettingsAsync();
    }

    partial void OnSelectedVersionSelectorRowChanged(LaunchVersionListRow? value)
    {
        if (_isSyncingVersionSelectorRow)
        {
            return;
        }

        if (value?.Instance is not null)
        {
            SelectVersion(value.Instance);
        }
    }




}
