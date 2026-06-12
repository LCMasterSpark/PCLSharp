using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LaunchPageViewModel
{
    private void RegisterCommands()
    {
        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshInstancesCommand = new AsyncRelayCommand(RefreshInstancesAsync);
        ScanJavaCommand = new AsyncRelayCommand(ScanJavaAsync);
        BrowseMinecraftRootCommand = new RelayCommand(BrowseMinecraftRoot);
        RemoveMinecraftRootCommand = new RelayCommand(RemoveSelectedMinecraftRoot, () => SelectedMinecraftRootFolder?.Type != MinecraftRootFolderType.Vanilla);
        RenameMinecraftRootCommand = new RelayCommand(RenameSelectedMinecraftRoot, () => SelectedMinecraftRootFolder is not null);
        OpenMinecraftRootCommand = new RelayCommand(OpenSelectedMinecraftRoot, () => SelectedMinecraftRootFolder is not null);
        BrowseJavaCommand = new AsyncRelayCommand(BrowseJavaAsync);
        BrowseLegacySkinCommand = new RelayCommand(BrowseLegacySkin, () => IsLegacySkinBrowseVisible);
        GenerateProfileCommand = new AsyncRelayCommand(GenerateProfileAsync);
        ExportLaunchScriptCommand = new AsyncRelayCommand(ExportLaunchScriptAsync);
        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync);
        CancelBusyCommand = new RelayCommand(CancelBusyOperation, () => IsBusy && _busyCancellation is not null);
        LoginMicrosoftAccountCommand = new AsyncRelayCommand(LoginMicrosoftAccountAsync, () => CanStartMicrosoftLogin);
        RefreshMicrosoftAccountCommand = new AsyncRelayCommand(RefreshMicrosoftAccountAsync, () => CanRefreshMicrosoftLogin);
        LogoutMicrosoftAccountCommand = new AsyncRelayCommand(LogoutMicrosoftAccountAsync, () => HasMicrosoftAccount);
        ToggleMicrosoftClientIdEditorCommand = new RelayCommand(ToggleMicrosoftClientIdEditor);
        SwitchMicrosoftAccountCommand = new RelayCommand(SwitchMicrosoftAccount, () => IsMicrosoftLogin && SelectedMicrosoftAccount is not null && !IsBusy);
        DeleteMicrosoftAccountCommand = new RelayCommand(DeleteSelectedMicrosoftAccount, () => IsMicrosoftLogin && SelectedMicrosoftAccount is not null && !IsBusy);
        LoginServerAccountCommand = new AsyncRelayCommand(LoginServerAccountAsync, () => IsServerLogin && !IsBusy && _loginService is not null);
        LogoutServerAccountCommand = new AsyncRelayCommand(LogoutServerAccountAsync, () => HasServerAccount);
        OpenMicrosoftDeviceCodePageCommand = new RelayCommand(OpenMicrosoftDeviceCodePage, () => IsMicrosoftDeviceCodeActive);
        CopyMicrosoftDeviceCodeCommand = new RelayCommand(CopyMicrosoftDeviceCode, () => IsMicrosoftDeviceCodeActive);
        OpenVersionSelectorCommand = new RelayCommand(OpenVersionSelector);
        CloseVersionSelectorCommand = new RelayCommand(CloseVersionSelector);
        SelectVersionCommand = new RelayCommand<MinecraftInstance>(SelectVersion);
        ToggleVersionStarCommand = new AsyncRelayCommand<MinecraftInstance>(ToggleVersionStarAsync, instance => instance is not null);
        ToggleVersionHiddenCommand = new AsyncRelayCommand<MinecraftInstance>(ToggleVersionHiddenAsync, instance => instance is not null);
        DeleteVersionCommand = new AsyncRelayCommand<MinecraftInstance>(DeleteVersionAsync, instance => instance is not null);
        OpenVersionFolderCommand = new RelayCommand<MinecraftInstance>(OpenVersionFolder, instance => instance is not null);
    }
}
