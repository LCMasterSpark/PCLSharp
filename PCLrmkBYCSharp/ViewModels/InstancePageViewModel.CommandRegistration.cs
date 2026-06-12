using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class InstancePageViewModel
{
    private void RegisterCommands()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        BrowseMinecraftRootCommand = new RelayCommand(BrowseMinecraftRoot);
        ImportInstanceCommand = new AsyncRelayCommand(ImportInstanceAsync);
        RemoveMinecraftRootCommand = new RelayCommand(RemoveSelectedMinecraftRoot, () => SelectedMinecraftRootFolder?.Type != MinecraftRootFolderType.Vanilla);
        RenameMinecraftRootCommand = new RelayCommand(RenameSelectedMinecraftRoot, () => SelectedMinecraftRootFolder is not null);
        OpenMinecraftRootCommand = new RelayCommand(OpenSelectedMinecraftRoot, () => SelectedMinecraftRootFolder is not null);
        SaveInstanceLaunchSettingsCommand = new AsyncRelayCommand(SaveInstanceLaunchSettingsAsync);
        ResetInstanceLaunchSettingsCommand = new AsyncRelayCommand(ResetInstanceLaunchSettingsAsync, () => SelectedInstance is not null);
        CompleteSelectedInstanceFilesCommand = new AsyncRelayCommand(CompleteSelectedInstanceFilesAsync, CanCompleteSelectedInstanceFiles);
        BrowseJavaCommand = new RelayCommand(BrowseJava);
        UseSelectedInstanceForLaunchCommand = new RelayCommand(UseSelectedInstanceForLaunch, () => SelectedInstance is not null && !IsSelectedInstanceLaunchVersion);
        UseInstanceForLaunchFromListCommand = new RelayCommand<MinecraftInstance>(UseInstanceForLaunchFromList, instance => instance is not null);
        OpenSelectedInstanceFolderCommand = new RelayCommand(OpenSelectedInstanceFolder, () => SelectedInstance is not null);
        OpenInstanceFolderFromListCommand = new RelayCommand<MinecraftInstance>(OpenInstanceFolderFromList, instance => instance is not null);
        OpenSelectedSavesFolderCommand = new RelayCommand(OpenSelectedSavesFolder, () => SelectedInstance is not null);
        OpenSelectedModsFolderCommand = new RelayCommand(OpenSelectedModsFolder, () => SelectedInstance is not null);
        OpenSelectedResourcePacksFolderCommand = new RelayCommand(OpenSelectedResourcePacksFolder, () => SelectedInstance is not null);
        OpenSelectedShaderPacksFolderCommand = new RelayCommand(OpenSelectedShaderPacksFolder, () => SelectedInstance is not null);
        OpenSelectedScreenshotsFolderCommand = new RelayCommand(OpenSelectedScreenshotsFolder, () => SelectedInstance is not null);
        RenameSelectedInstanceCommand = new AsyncRelayCommand(RenameSelectedInstanceAsync, () => SelectedInstance is not null);
        RenameInstanceFromListCommand = new AsyncRelayCommand<MinecraftInstance>(RenameInstanceFromListAsync, instance => instance is not null);
        CloneSelectedInstanceCommand = new AsyncRelayCommand(CloneSelectedInstanceAsync, () => SelectedInstance is not null);
        CloneInstanceFromListCommand = new AsyncRelayCommand<MinecraftInstance>(CloneInstanceFromListAsync, instance => instance is not null);
        ExportSelectedInstanceScriptCommand = new AsyncRelayCommand(ExportSelectedInstanceScriptAsync, () => SelectedInstance is not null);
        ExportSelectedInstanceModpackCommand = new AsyncRelayCommand(ExportSelectedInstanceModpackAsync, () => SelectedInstance is not null);
        ToggleSelectedInstanceStarCommand = new AsyncRelayCommand(ToggleSelectedInstanceStarAsync, () => SelectedInstance is not null);
        ToggleInstanceStarFromListCommand = new AsyncRelayCommand<MinecraftInstance>(ToggleInstanceStarFromListAsync, instance => instance is not null);
        ToggleSelectedInstanceHiddenCommand = new AsyncRelayCommand(ToggleSelectedInstanceHiddenAsync, () => SelectedInstance is not null);
        ToggleInstanceHiddenFromListCommand = new AsyncRelayCommand<MinecraftInstance>(ToggleInstanceHiddenFromListAsync, instance => instance is not null);
        DeleteSelectedInstanceCommand = new AsyncRelayCommand(DeleteSelectedInstanceAsync, () => SelectedInstance is not null);
        DeleteInstanceFromListCommand = new AsyncRelayCommand<MinecraftInstance>(DeleteInstanceFromListAsync, instance => instance is not null);
        SelectInstanceCommand = new RelayCommand<MinecraftInstance>(SelectInstance);
        RefreshLocalModsCommand = new AsyncRelayCommand(RefreshLocalModsAsync, () => SelectedInstance is not null);
        CheckLocalModUpdatesCommand = new AsyncRelayCommand(CheckLocalModUpdatesAsync, CanCheckLocalModUpdates);
        UpdateSelectedLocalModsCommand = new AsyncRelayCommand(UpdateSelectedLocalModsAsync, () => SelectedUpdateLocalModCount > 0);
        UpdateAllLocalModsCommand = new AsyncRelayCommand(UpdateAllLocalModsAsync, () => UpdateLocalModCount > 0);
        InstallLocalModsCommand = new AsyncRelayCommand(InstallLocalModsAsync, () => SelectedInstance is not null);
        DownloadModsForSelectedInstanceCommand = new AsyncRelayCommand(DownloadModsForSelectedInstanceAsync, () => SelectedInstance is not null);
        ToggleLocalModSelectionCommand = new RelayCommand<LocalModListRow>(ToggleLocalModSelection);
        SelectAllLocalModsCommand = new RelayCommand(SelectAllLocalMods, () => LocalMods.Count > 0);
        ClearSelectedLocalModsCommand = new RelayCommand(ClearSelectedLocalMods, () => SelectedLocalModCount > 0);
        EnableSelectedLocalModsCommand = new AsyncRelayCommand(EnableSelectedLocalModsAsync, () => SelectedDisabledLocalModCount > 0);
        DisableSelectedLocalModsCommand = new AsyncRelayCommand(DisableSelectedLocalModsAsync, () => SelectedEnabledLocalModCount > 0);
        DeleteSelectedLocalModsCommand = new AsyncRelayCommand(DeleteSelectedLocalModsAsync, () => SelectedLocalModCount > 0);
        ToggleSelectedLocalModEnabledCommand = new AsyncRelayCommand(ToggleSelectedLocalModEnabledAsync, () => SelectedLocalMod is not null);
        DeleteSelectedLocalModCommand = new AsyncRelayCommand(DeleteSelectedLocalModAsync, () => SelectedLocalMod is not null);
    }
}
