using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel : PageViewModelBase
{
    private readonly IMinecraftClientDownloadService _minecraftClientDownload;
    private readonly IDownloadManagerService _downloadManager;
    private readonly ICommunityResourceSearchService _communityResourceSearch;
    private readonly ICommunityResourceVersionService _communityResourceVersions;
    private readonly IModpackInstallService _modpackInstall;
    private readonly ILoaderProcessorRunner _processorRunner;
    private readonly ILoaderVersionService? _loaderVersions;
    private readonly IFabricLoaderInstallService? _fabricLoaderInstall;
    private readonly IQuiltLoaderInstallService? _quiltLoaderInstall;
    private readonly IForgeLoaderInstallService? _forgeLoaderInstall;
    private readonly INeoForgeLoaderInstallService? _neoForgeLoaderInstall;
    private readonly IAppSettingsService _settings;
    private readonly IMinecraftDiscoveryService _minecraftDiscovery;
    private readonly IFileDialogService _fileDialogs;
    private readonly IMinecraftRootFolderService _rootFolders;
    private readonly IMinecraftSelectionService _selections;
    private readonly IMinecraftGameDirectoryService _gameDirectories;
    private readonly IUserPromptService _prompts;
    private readonly IFolderOpenService _folders;
    private readonly IExternalUrlService _urls;
    private readonly IAppLoggerService _logger;
    private readonly IUiDispatcherService? _dispatcher;
    private readonly SynchronizationContext? _uiContext = CaptureUiSynchronizationContext();
    private readonly List<CommunityResourceProject> _lastResourceProjects = [];
    private readonly object _versionsSync = new();
    private readonly object _versionCategoryItemsSync = new();
    private readonly object _downloadTasksSync = new();
    private readonly object _resourceProjectsSync = new();
    private readonly object _resourceVersionsSync = new();
    private readonly object _loaderVersionsSync = new();
    private readonly object _minecraftRootFoldersSync = new();
    private bool _isSyncingRootFolderSelection;
    private bool _hasLoadedVersionManifest;
    private bool _hasAppliedVersionManifestToUi;
    private bool _isAutoLoadingResourceVersions;
    private readonly List<MinecraftRemoteVersion> _allVersions = [];

    [ObservableProperty]
    private string minecraftRootPath;

    [ObservableProperty]
    private MinecraftRootFolder? selectedMinecraftRootFolder;

    [ObservableProperty]
    private MinecraftRemoteVersion? selectedVersion;

    [ObservableProperty]
    private DownloadSectionItem selectedSection;

    [ObservableProperty]
    private string instanceName = "";

    [ObservableProperty]
    private string selectedLoaderKind = "Fabric";

    [ObservableProperty]
    private string loaderVersion = "";

    [ObservableProperty]
    private LoaderVersionOption? selectedLoaderVersion;

    [ObservableProperty]
    private string resourceSearchText = "";

    [ObservableProperty]
    private string resourceGameVersion = "";

    [ObservableProperty]
    private string resourceLoader = "";

    [ObservableProperty]
    private string selectedResourceSource = "全部";

    [ObservableProperty]
    private string selectedInstallMode = "原版安装";

    [ObservableProperty]
    private string selectedVersionCategory = "全部版本";

    [ObservableProperty]
    private CommunityResourceProject? selectedResourceProject;

    [ObservableProperty]
    private CommunityResourceVersion? selectedResourceVersion;

    [ObservableProperty]
    private CommunityResourceFile? selectedResourceFile;

    [ObservableProperty]
    private DownloadTaskSnapshot? selectedDownloadTask;

    [ObservableProperty]
    private string statusMessage = "等待刷新版本列表";

    [ObservableProperty]
    private string resourceInstallTarget = "资源将安装到当前启动实例";

    [ObservableProperty]
    private bool isBusy;

    public DownloadPageViewModel(
        IMinecraftClientDownloadService minecraftClientDownload,
        IDownloadManagerService downloadManager,
        ICommunityResourceSearchService communityResourceSearch,
        ICommunityResourceVersionService communityResourceVersions,
        IModpackInstallService modpackInstall,
        ILoaderProcessorRunner processorRunner,
        IAppSettingsService settings,
        IMinecraftDiscoveryService minecraftDiscovery,
        IFileDialogService fileDialogs,
        IAppLoggerService logger,
        IMinecraftRootFolderService? rootFolders = null,
        IMinecraftSelectionService? selections = null,
        IUserPromptService? prompts = null,
        IMinecraftGameDirectoryService? gameDirectories = null,
        ILoaderVersionService? loaderVersions = null,
        IFabricLoaderInstallService? fabricLoaderInstall = null,
        IQuiltLoaderInstallService? quiltLoaderInstall = null,
        IForgeLoaderInstallService? forgeLoaderInstall = null,
        INeoForgeLoaderInstallService? neoForgeLoaderInstall = null,
        IFolderOpenService? folders = null,
        IExternalUrlService? urls = null,
        IUiDispatcherService? dispatcher = null)
        : base(PageRoute.Download, "下载", "原版游戏、社区资源与下载管理")
    {
        _minecraftClientDownload = minecraftClientDownload;
        _downloadManager = downloadManager;
        _communityResourceSearch = communityResourceSearch;
        _communityResourceVersions = communityResourceVersions;
        _modpackInstall = modpackInstall;
        _processorRunner = processorRunner;
        _loaderVersions = loaderVersions;
        _fabricLoaderInstall = fabricLoaderInstall;
        _quiltLoaderInstall = quiltLoaderInstall;
        _forgeLoaderInstall = forgeLoaderInstall;
        _neoForgeLoaderInstall = neoForgeLoaderInstall;
        _settings = settings;
        _minecraftDiscovery = minecraftDiscovery;
        _fileDialogs = fileDialogs;
        _rootFolders = rootFolders ?? new MinecraftRootFolderService(settings);
        _selections = selections ?? new MinecraftSelectionService();
        _gameDirectories = gameDirectories ?? new MinecraftGameDirectoryService(settings);
        _prompts = prompts ?? new UserPromptService();
        _folders = folders ?? new FolderOpenService();
        _urls = urls ?? new ExternalUrlService();
        _logger = logger;
        _dispatcher = dispatcher;

        EnableCollectionSynchronization();
        var savedRoot = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        minecraftRootPath = string.IsNullOrWhiteSpace(savedRoot) ? _minecraftDiscovery.GetDefaultMinecraftRoot() : savedRoot;
        RefreshMinecraftRootFolders();
        selectedSection = Sections[0];

        RegisterCommands();
        _downloadManager.SnapshotChanged += (_, _) => RefreshTaskSnapshotsOnUi();
        RefreshTaskSnapshots();
        UpdateResourceInstallTargetLabel();
    }

    private void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(Versions, _versionsSync);
        BindingOperations.EnableCollectionSynchronization(VersionCategoryItems, _versionCategoryItemsSync);
        BindingOperations.EnableCollectionSynchronization(DownloadTasks, _downloadTasksSync);
        BindingOperations.EnableCollectionSynchronization(ResourceProjects, _resourceProjectsSync);
        BindingOperations.EnableCollectionSynchronization(ResourceVersions, _resourceVersionsSync);
        BindingOperations.EnableCollectionSynchronization(LoaderVersions, _loaderVersionsSync);
        BindingOperations.EnableCollectionSynchronization(MinecraftRootFolders, _minecraftRootFoldersSync);
    }

    private static SynchronizationContext? CaptureUiSynchronizationContext()
    {
        var context = SynchronizationContext.Current;
        return context?.GetType().FullName == "System.Windows.Threading.DispatcherSynchronizationContext"
            ? context
            : null;
    }
}
