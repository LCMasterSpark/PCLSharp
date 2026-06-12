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

public sealed partial class InstancePageViewModel : PageViewModelBase
{
    public sealed record InstanceDetailSectionOption(int Value, string DisplayName, string Description)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record IntOption(int Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record BoolOption(bool Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record DisplayTypeOption(MinecraftInstanceDisplayType Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly IMinecraftDiscoveryService _minecraftDiscovery;
    private readonly IMinecraftInstanceManagementService _instanceManagement;
    private readonly ILaunchFileCompleter _fileCompleter;
    private readonly ILaunchPipelineService _launchPipeline;
    private readonly IDownloadManagerService _downloadManager;
    private readonly IModpackExportService _modpackExport;
    private readonly IAppSettingsService _settings;
    private readonly IFileDialogService _fileDialogs;
    private readonly IUserPromptService _prompts;
    private readonly IAppLoggerService _logger;
    private readonly ILocalModService _localMods;
    private readonly ILocalModUpdateService? _localModUpdates;
    private readonly IMinecraftGameDirectoryService _gameDirectories;
    private readonly IMinecraftRootFolderService _rootFolders;
    private readonly IMinecraftSelectionService _selections;
    private readonly IFolderOpenService _folders;
    private readonly object _instancesSync = new();
    private readonly object _instanceRowsSync = new();
    private readonly object _localModsSync = new();
    private readonly object _fileCompletionDetailsSync = new();
    private readonly object _minecraftRootFoldersSync = new();
    private readonly List<MinecraftInstance> _allInstances = [];
    private static readonly string[] InstanceLaunchSettingKeys =
    [
        AppSettingKeys.LaunchMinMemoryMb,
        AppSettingKeys.LaunchMaxMemoryMb,
        AppSettingKeys.VersionRamType,
        AppSettingKeys.VersionRamCustom,
        AppSettingKeys.LaunchWindowWidth,
        AppSettingKeys.LaunchWindowHeight,
        AppSettingKeys.VersionArgumentTitle,
        AppSettingKeys.VersionAdvanceJvm,
        AppSettingKeys.VersionAdvanceGame,
        AppSettingKeys.VersionAdvanceRun,
        AppSettingKeys.VersionAdvanceRunWait,
        AppSettingKeys.VersionArgumentJavaSelect,
        AppSettingKeys.VersionArgumentInfo,
        AppSettingKeys.VersionServerEnter,
        AppSettingKeys.VersionServerLogin,
        AppSettingKeys.VersionServerNide,
        AppSettingKeys.VersionServerAuthServer,
        AppSettingKeys.VersionServerAuthRegister,
        AppSettingKeys.VersionServerAuthName,
        AppSettingKeys.VersionAdvanceGC,
        AppSettingKeys.VersionRamOptimize,
        AppSettingKeys.VersionAdvanceDisableJLW,
        AppSettingKeys.VersionAdvanceDisableLUA,
        AppSettingKeys.VersionAdvanceDisableModUpdate,
        AppSettingKeys.VersionAdvanceJava,
        AppSettingKeys.VersionAdvanceAssetsV2,
        AppSettingKeys.VersionArgumentIndieV2
    ];
    private readonly List<LocalModFile> _allLocalMods = [];
    private readonly HashSet<string> _selectedLocalModKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LocalModUpdateInfo> _localModUpdateInfos = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoadingLaunchSettings;
    private bool _isSyncingRootFolderSelection;
    private bool _isChangingRootPathFromSelection;
    private bool _isRestoringSelection;
    private bool _isSyncingInstanceRowSelection;
    private bool _suppressHiddenSelectionReveal;

    private sealed record LocalModUpdateDownload(LocalModFile Mod, LocalModUpdateInfo Info, DownloadFile Download);

    public InstancePageViewModel(
        IMinecraftDiscoveryService minecraftDiscovery,
        IMinecraftInstanceManagementService instanceManagement,
        ILaunchFileCompleter fileCompleter,
        ILaunchPipelineService launchPipeline,
        IDownloadManagerService downloadManager,
        IModpackExportService? modpackExport,
        IAppSettingsService settings,
        IFileDialogService fileDialogs,
        IUserPromptService prompts,
        IAppLoggerService logger,
        IMinecraftGameDirectoryService? gameDirectories = null,
        IMinecraftRootFolderService? rootFolders = null,
        IMinecraftSelectionService? selections = null,
        ILocalModService? localModService = null,
        ILocalModUpdateService? localModUpdateService = null,
        IFolderOpenService? folders = null,
        IUiDispatcherService? dispatcher = null)
        : base(PageRoute.Instance, "实例", "本地版本、实例状态与实例单独启动设置")
    {
        _minecraftDiscovery = minecraftDiscovery;
        _instanceManagement = instanceManagement;
        _fileCompleter = fileCompleter;
        _launchPipeline = launchPipeline;
        _downloadManager = downloadManager;
        _modpackExport = modpackExport ?? new ModpackExportService(logger);
        _settings = settings;
        _fileDialogs = fileDialogs;
        _prompts = prompts;
        _logger = logger;
        _localMods = localModService ?? new LocalModService(logger);
        _localModUpdates = localModUpdateService;
        _gameDirectories = gameDirectories ?? new MinecraftGameDirectoryService(settings);
        _rootFolders = rootFolders ?? new MinecraftRootFolderService(settings);
        _selections = selections ?? new MinecraftSelectionService();
        _folders = folders ?? new FolderOpenService();
        EnableCollectionSynchronization();

        var savedRoot = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        minecraftRootPath = string.IsNullOrWhiteSpace(savedRoot)
            ? _minecraftDiscovery.GetDefaultMinecraftRoot()
            : savedRoot;
        versionSortMode = NormalizeVersionSortMode(_settings.Get(AppSettingKeys.VersionSortMode, 0));
        RefreshMinecraftRootFolders();
        LoadLaunchSettings(instanceName: null);

        RegisterCommands();
    }

}
