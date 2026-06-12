using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel
{
    private void RegisterCommands()
    {
        RefreshVersionsCommand = new AsyncRelayCommand(RefreshVersionsAsync);
        RefreshLoaderVersionsCommand = new AsyncRelayCommand(RefreshLoaderVersionsAsync);
        InstallSelectedVersionCommand = new AsyncRelayCommand(InstallSelectedVersionAsync);
        InstallSelectedLoaderCommand = new AsyncRelayCommand(InstallSelectedLoaderAsync);
        BrowseMinecraftRootCommand = new RelayCommand(BrowseMinecraftRoot);
        RemoveMinecraftRootCommand = new RelayCommand(RemoveSelectedMinecraftRoot, () => SelectedMinecraftRootFolder?.Type != MinecraftRootFolderType.Vanilla);
        RenameMinecraftRootCommand = new RelayCommand(RenameSelectedMinecraftRoot, () => SelectedMinecraftRootFolder is not null);
        OpenMinecraftRootCommand = new RelayCommand(OpenSelectedMinecraftRoot, () => SelectedMinecraftRootFolder is not null);
        RefreshCurrentSectionCommand = new AsyncRelayCommand(RefreshCurrentSectionAsync);
        RefreshTaskSnapshotsCommand = new RelayCommand(RefreshTaskSnapshots);
        SearchResourcesCommand = new AsyncRelayCommand(SearchResourcesAsync);
        LoadResourceVersionsCommand = new AsyncRelayCommand(LoadResourceVersionsAsync);
        DownloadSelectedResourceFileCommand = new AsyncRelayCommand(DownloadSelectedResourceFileAsync, () => CanDownloadSelectedResourceFile);
        InstallLocalModpackCommand = new AsyncRelayCommand(InstallLocalModpackAsync);
        OpenSelectedResourceProjectCommand = new RelayCommand(OpenSelectedResourceProject, () => !string.IsNullOrWhiteSpace(SelectedResourceProject?.WebsiteUrl));
        CancelSelectedDownloadTaskCommand = new RelayCommand(CancelSelectedDownloadTask, () => SelectedDownloadTask?.CanCancel == true);
        RetrySelectedDownloadTaskCommand = new AsyncRelayCommand(RetrySelectedDownloadTaskAsync, () => SelectedDownloadTask?.CanRetry == true);
        OpenSelectedDownloadTaskFolderCommand = new RelayCommand(OpenSelectedDownloadTaskFolder, () => !string.IsNullOrWhiteSpace(SelectedDownloadTask?.PrimaryLocalPath));
        CancelAllRunningDownloadTasksCommand = new RelayCommand(CancelAllRunningDownloadTasks, () => RunningTaskCount > 0);
        ClearFinishedDownloadTasksCommand = new RelayCommand(ClearFinishedDownloadTasks, () => FinishedTaskCount > 0);
        OpenDownloadManagerCommand = new RelayCommand(() =>
            SelectedSection = Sections.Single(section => section.Section == DownloadSection.Manager));
    }
}
