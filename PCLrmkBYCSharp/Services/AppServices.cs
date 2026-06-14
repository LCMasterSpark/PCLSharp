using System.IO;
using System.Windows.Threading;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.FeatureHub;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.Services.Link;

namespace PCLrmkBYCSharp.Services;

public sealed class AppServices
{
    private AppServices(
        IAppPathService paths,
        IAppLoggerService logger,
        IUiDispatcherService dispatcher,
        IFileDialogService fileDialogs,
        IAppSettingsService settings,
        IMinecraftDiscoveryService minecraftDiscovery,
        IDownloadManagerService downloadManager,
        IMinecraftClientDownloadService minecraftClientDownload,
        ICommunityResourceSearchService communityResourceSearch,
        ICommunityResourceVersionService communityResourceVersions,
        IModpackInstallService modpackInstall,
        ILoaderProcessorRunner loaderProcessorRunner,
        IJavaDiscoveryService javaDiscovery,
        ILaunchPipelineService launchPipeline,
        IAppExitGuardService exitGuard,
        UserPromptService prompts,
        INavigationService navigation)
    {
        Paths = paths;
        Logger = logger;
        Dispatcher = dispatcher;
        FileDialogs = fileDialogs;
        Settings = settings;
        MinecraftDiscovery = minecraftDiscovery;
        DownloadManager = downloadManager;
        MinecraftClientDownload = minecraftClientDownload;
        CommunityResourceSearch = communityResourceSearch;
        CommunityResourceVersions = communityResourceVersions;
        ModpackInstall = modpackInstall;
        LoaderProcessorRunner = loaderProcessorRunner;
        JavaDiscovery = javaDiscovery;
        LaunchPipeline = launchPipeline;
        ExitGuard = exitGuard;
        Prompts = prompts;
        Navigation = navigation;
    }

    public IAppPathService Paths { get; }

    public IAppLoggerService Logger { get; }

    public IUiDispatcherService Dispatcher { get; }

    public IFileDialogService FileDialogs { get; }

    public IAppSettingsService Settings { get; }

    public IMinecraftDiscoveryService MinecraftDiscovery { get; }

    public IDownloadManagerService DownloadManager { get; }

    public IMinecraftClientDownloadService MinecraftClientDownload { get; }

    public ICommunityResourceSearchService CommunityResourceSearch { get; }

    public ICommunityResourceVersionService CommunityResourceVersions { get; }

    public IModpackInstallService ModpackInstall { get; }

    public ILoaderProcessorRunner LoaderProcessorRunner { get; }

    public IJavaDiscoveryService JavaDiscovery { get; }

    public ILaunchPipelineService LaunchPipeline { get; }

    public IAppExitGuardService ExitGuard { get; }

    public UserPromptService Prompts { get; }

    public INavigationService Navigation { get; }

    public static AppServices Create(Dispatcher dispatcher)
    {
        var paths = new AppPathService();
        var logger = new AppLoggerService(paths);
        var uiDispatcher = new UiDispatcherService(dispatcher);
        var fileDialogs = new FileDialogService();
        var folders = new FolderOpenService();
        var urls = new ExternalUrlService();
        var clipboard = new ClipboardService();
        var prompts = new UserPromptService();
        var settings = new AppSettingsService(paths);
        var help = new HelpService(logger, customHelpDirectories:
        [
            Path.Combine(AppContext.BaseDirectory, "PCL", "Help"),
            Path.Combine(paths.AppDataDirectory, "Help")
        ]);
        var helpActions = new HelpActionService(
            showMessage: (title, message) => prompts.Confirm(title, message),
            showHint: (message, _) => prompts.Confirm("提示", message),
            setClipboardText: clipboard.SetText,
            settings: settings);
        var linkService = new PclLinkService();
        var linkBackend = new LinkBackendService(new LinkPortAllocator());
        var linkProcess = new LinkProcessService(new LinkProcessRunner(), logger);
        var instanceManagement = new MinecraftInstanceManagementService();
        var minecraftDiscovery = new MinecraftDiscoveryService(instanceManagement);
        var gameDirectories = new MinecraftGameDirectoryService(settings);
        var rootFolders = new MinecraftRootFolderService(settings);
        var selections = new MinecraftSelectionService();
        var fileChecker = new FileCheckService(logger);
        var downloadByteClient = new DownloadByteClient();
        var localModUpdates = new LocalModUpdateService(downloadByteClient, logger);
        var downloadSources = new DownloadSourceService(settings);
        var downloadManager = new DownloadManagerService(downloadByteClient, fileChecker, logger, settings);
        var exitGuard = new AppExitGuardService(downloadManager, prompts, logger);
        var minecraftClientDownload = new MinecraftClientDownloadService(downloadByteClient, downloadSources, settings);
        var communityResourceSearch = new CommunityResourceSearchService(downloadByteClient, logger, settings);
        var communityResourceVersions = new CommunityResourceVersionService(downloadByteClient, downloadSources, logger, settings);
        var loaderVersions = new LoaderVersionService(downloadByteClient, logger);
        var fabricLoaderInstall = new FabricLoaderInstallService(downloadByteClient, downloadSources, logger);
        var quiltLoaderInstall = new QuiltLoaderInstallService(downloadByteClient, downloadSources, logger);
        var forgeLoaderInstall = new ForgeLoaderInstallService(downloadByteClient, downloadSources, logger);
        var neoForgeLoaderInstall = new NeoForgeLoaderInstallService(downloadByteClient, downloadSources, logger);
        var modpackInstall = new ModpackInstallService(minecraftClientDownload, fabricLoaderInstall, quiltLoaderInstall, forgeLoaderInstall, neoForgeLoaderInstall, downloadSources, logger, downloadByteClient);
        var modpackExport = new ModpackExportService(logger);
        var javaDiscovery = new JavaDiscoveryService(logger);
        var javaSelector = new JavaSelectorService();
        var legacyLogin = new LegacyLoginService();
        var http = new LaunchHttpClient();
        var updateCheck = new AppUpdateCheckService(http);
        var featureHub = new FeatureHubService(paths, settings);
        var mojangProfiles = new MojangProfileService(http, settings, logger);
        var microsoftDeviceCodes = new WpfMicrosoftDeviceCodePresenter(clipboard);
        var microsoftLogin = new MicrosoftLoginService(http, settings, microsoftDeviceCodes);
        var yggdrasilLogin = new YggdrasilLoginService(http, settings, new WpfYggdrasilProfileSelector());
        var login = new LoginService(legacyLogin, microsoftLogin, yggdrasilLogin, settings, mojangProfiles);
        var argumentBuilder = new LaunchArgumentBuilder(settings, gameDirectories, new SystemMemoryService());
        var fileCompleter = new LaunchFileCompleter(downloadSources, fileChecker, logger, http);
        var nativesExtractor = new NativesExtractor(logger);
        var preRun = new LaunchPreRunService(settings, logger, gameDirectories, paths);
        var patches = new LaunchPatchService(logger);
        var customCommand = new CustomCommandService(settings, logger, gameDirectories: gameDirectories);
        var scriptExporter = new LaunchScriptExporter(settings);
        var processLauncher = new ProcessLauncher();
        var processConfigurator = new LaunchProcessConfigurator(settings, logger);
        var memoryOptimizer = new WindowsLaunchMemoryOptimizer(logger);
        var loaderProcessorRunner = new LoaderProcessorRunner(processLauncher, logger);
        var watcher = new GameProcessWatcher(logger);
        var gameWindow = new GameWindowService(logger);
        var launcherVisibility = new LauncherVisibilityService(logger, new WpfLauncherWindowHost());
        var windowTitle = new LaunchWindowTitleService(settings);
        var launchPipeline = new LaunchPipelineService(
            javaDiscovery,
            javaSelector,
            login,
            argumentBuilder,
            fileCompleter,
            downloadManager,
            nativesExtractor,
            preRun,
            patches,
            customCommand,
            scriptExporter,
            processLauncher,
            processConfigurator,
            watcher,
            logger,
            gameWindow,
            launcherVisibility,
            windowTitle,
            gameDirectories,
            settings,
            memoryOptimizer);
        var navigation = new NavigationService(settings, paths, fileDialogs, minecraftDiscovery, instanceManagement, gameDirectories, rootFolders, selections, downloadManager, minecraftClientDownload, communityResourceSearch, communityResourceVersions, modpackInstall, modpackExport, loaderProcessorRunner, fileCompleter, localModUpdates, javaDiscovery, javaSelector, launchPipeline, legacyLogin, login, prompts, uiDispatcher, logger, help, helpActions, linkService, linkBackend, linkProcess, clipboard, updateCheck, featureHub, folders, urls, memoryOptimizer, loaderVersions, fabricLoaderInstall, quiltLoaderInstall, forgeLoaderInstall, neoForgeLoaderInstall, microsoftDeviceCodes);
        helpActions.SetEventHandler(HelpActionService.EventSwitchPage, (eventData, cancellationToken) =>
        {
            if (!HelpActionService.TryMapOldPclPageRoute(eventData.Split('|', StringSplitOptions.TrimEntries).FirstOrDefault() ?? "", out var route, out var message))
            {
                return Task.FromResult(new HelpActionResult(false, message));
            }

            cancellationToken.ThrowIfCancellationRequested();
            uiDispatcher.Invoke(() => navigation.Navigate(route));
            return Task.FromResult(new HelpActionResult(true, "已切换页面：" + route));
        });
        helpActions.SetEventHandler(HelpActionService.EventJoinRoom, async (eventData, cancellationToken) =>
        {
            settings.Set(AppSettingKeys.LinkLastInviteCode, eventData.Trim());
            await settings.SaveAsync(cancellationToken);
            uiDispatcher.Invoke(() => navigation.Navigate(PageRoute.Link));
            return new HelpActionResult(true, "已打开陶瓦联机页面");
        });
        helpActions.SetEventHandler(HelpActionService.EventImportModpack, (eventData, cancellationToken) =>
            OpenModpackDownloadPresetAsync(eventData, "已打开整合包导入入口", settings, navigation, uiDispatcher, cancellationToken));
        helpActions.SetEventHandler(HelpActionService.EventInstallModpack, (eventData, cancellationToken) =>
            OpenModpackDownloadPresetAsync(eventData, "已打开整合包安装入口", settings, navigation, uiDispatcher, cancellationToken));
        helpActions.SetEventHandler(HelpActionService.EventCheckUpdate, (eventData, cancellationToken) =>
            CheckAppUpdateAsync(eventData, updateCheck, cancellationToken));
        return new AppServices(paths, logger, uiDispatcher, fileDialogs, settings, minecraftDiscovery, downloadManager, minecraftClientDownload, communityResourceSearch, communityResourceVersions, modpackInstall, loaderProcessorRunner, javaDiscovery, launchPipeline, exitGuard, prompts, navigation);
    }

    private static async Task<HelpActionResult> OpenModpackDownloadPresetAsync(
        string eventData,
        string successMessage,
        IAppSettingsService settings,
        INavigationService navigation,
        IUiDispatcherService uiDispatcher,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preset = HelpActionService.ParseModpackDownloadPreset(eventData);
        settings.Set(AppSettingKeys.DownloadPresetResourceSection, (int)DownloadSection.ModPack);
        settings.Set(AppSettingKeys.DownloadPresetSearchText, preset.SearchText);
        settings.Set(AppSettingKeys.DownloadPresetGameVersion, preset.GameVersion);
        settings.Set(AppSettingKeys.DownloadPresetLoader, preset.Loader);
        await settings.SaveAsync(cancellationToken);
        uiDispatcher.Invoke(() => navigation.Navigate(PageRoute.Download));
        var detail = string.IsNullOrWhiteSpace(preset.SearchText) ? "" : "：" + preset.SearchText;
        return new HelpActionResult(true, successMessage + detail);
    }

    private static async Task<HelpActionResult> CheckAppUpdateAsync(
        string eventData,
        IAppUpdateCheckService updateCheck,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = await updateCheck.CheckAsync(eventData, cancellationToken);
            var lines = new List<string> { info.Summary };
            if (info.PublishedAt is not null)
            {
                lines.Add("发布时间：" + info.PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            }

            if (!string.IsNullOrWhiteSpace(info.ReleaseUrl))
            {
                lines.Add("发布页：" + info.ReleaseUrl);
            }

            return new HelpActionResult(true, string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            return new HelpActionResult(false, "检查更新失败：" + ex.Message);
        }
    }
}
