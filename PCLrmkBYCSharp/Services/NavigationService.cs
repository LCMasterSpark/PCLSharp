using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Services;

public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<PageRoute, PageViewModelBase> _pages;

    public NavigationService(
        IAppSettingsService settings,
        IAppPathService paths,
        IFileDialogService fileDialogs,
        IMinecraftDiscoveryService minecraftDiscovery,
        IMinecraftInstanceManagementService instanceManagement,
        IMinecraftGameDirectoryService gameDirectories,
        IMinecraftRootFolderService rootFolders,
        IMinecraftSelectionService selections,
        IDownloadManagerService downloadManager,
        IMinecraftClientDownloadService minecraftClientDownload,
        ICommunityResourceSearchService communityResourceSearch,
        ICommunityResourceVersionService communityResourceVersions,
        IModpackInstallService modpackInstall,
        IModpackExportService modpackExport,
        ILoaderProcessorRunner loaderProcessorRunner,
        ILaunchFileCompleter launchFileCompleter,
        ILocalModUpdateService localModUpdates,
        IJavaDiscoveryService javaDiscovery,
        ILaunchPipelineService launchPipeline,
        ILegacyLoginService legacyLogin,
        ILoginService loginService,
        IUserPromptService prompts,
        IUiDispatcherService dispatcher,
        IAppLoggerService logger,
        IHelpService help,
        IHelpActionService helpActions,
        IFolderOpenService? folders = null,
        IExternalUrlService? urls = null,
        ILoaderVersionService? loaderVersions = null,
        IFabricLoaderInstallService? fabricLoaderInstall = null,
        IQuiltLoaderInstallService? quiltLoaderInstall = null,
        IForgeLoaderInstallService? forgeLoaderInstall = null,
        INeoForgeLoaderInstallService? neoForgeLoaderInstall = null,
        IMicrosoftDeviceCodeStatusService? microsoftDeviceCodes = null)
    {
        folders ??= new FolderOpenService();
        Pages =
        [
            new(PageRoute.Launch, "启动", "账号、版本与启动链路"),
            new(PageRoute.Download, "下载", "游戏、组件与社区资源"),
            new(PageRoute.Instance, "实例", "本地版本与 Mod 管理"),
            new(PageRoute.Setup, "设置", "启动器全局设置"),
            new(PageRoute.Other, "更多", "帮助、关于与工具")
        ];

        var launchPage = new LaunchPageViewModel(
            minecraftDiscovery,
            javaDiscovery,
            launchPipeline,
            settings,
            fileDialogs,
            legacyLogin,
            logger,
            gameDirectories,
            rootFolders,
            selections,
            prompts,
            dispatcher,
            instanceManagement,
            folders,
            loginService,
            microsoftDeviceCodes);
        helpActions.SetEventHandler(HelpActionService.EventLaunchGame, launchPage.ExecuteCustomLaunchEventAsync);

        _pages = new Dictionary<PageRoute, PageViewModelBase>
        {
            [PageRoute.Launch] = launchPage,
            [PageRoute.Download] = new DownloadPageViewModel(minecraftClientDownload, downloadManager, communityResourceSearch, communityResourceVersions, modpackInstall, loaderProcessorRunner, settings, minecraftDiscovery, fileDialogs, logger, rootFolders, selections, prompts, gameDirectories, loaderVersions, fabricLoaderInstall, quiltLoaderInstall, forgeLoaderInstall, neoForgeLoaderInstall, folders, urls, dispatcher),
            [PageRoute.Instance] = new InstancePageViewModel(minecraftDiscovery, instanceManagement, launchFileCompleter, launchPipeline, downloadManager, modpackExport, settings, fileDialogs, prompts, logger, gameDirectories, rootFolders, selections, localModUpdateService: localModUpdates, folders: folders, dispatcher: dispatcher),
            [PageRoute.Setup] = new SetupPageViewModel(settings, paths, fileDialogs, logger),
            [PageRoute.Other] = new OtherPageViewModel(paths, help, logger, helpActions)
        };
        CurrentPage = _pages[PageRoute.Launch];
    }

    public IReadOnlyList<PageNavigationItem> Pages { get; }

    public PageViewModelBase CurrentPage { get; private set; }

    public Task PreloadDownloadVersionsAsync()
    {
        return _pages.TryGetValue(PageRoute.Download, out var page) && page is DownloadPageViewModel downloadPage
            ? downloadPage.PreloadVersionManifestAsync()
            : Task.CompletedTask;
    }

    public void Navigate(PageRoute route)
    {
        if (_pages.TryGetValue(route, out var page))
        {
            CurrentPage = page;
        }
    }
}
