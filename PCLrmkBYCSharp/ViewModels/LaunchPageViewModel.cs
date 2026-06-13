using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LaunchPageViewModel : PageViewModelBase
{
    public sealed record LoginTypeOption(LoginType Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record IntOption(int Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private const string UseGlobalJavaSettingText = "使用全局设置";
    private readonly IMinecraftDiscoveryService _minecraftDiscovery;
    private readonly IJavaDiscoveryService _javaDiscovery;
    private readonly IJavaSelectorService _javaSelector;
    private readonly ILaunchPipelineService _launchPipeline;
    private readonly IMinecraftInstanceManagementService _instanceManagement;
    private readonly IAppSettingsService _settings;
    private readonly IFileDialogService _fileDialogs;
    private readonly ILegacyLoginService _legacyLogin;
    private readonly ILoginService? _loginService;
    private readonly IAppLoggerService _logger;
    private readonly IMinecraftGameDirectoryService _gameDirectories;
    private readonly IMinecraftRootFolderService _rootFolders;
    private readonly IMinecraftSelectionService _selections;
    private readonly IUserPromptService _prompts;
    private readonly IUiDispatcherService? _dispatcher;
    private readonly IFolderOpenService _folders;
    private readonly IMicrosoftDeviceCodeStatusService? _microsoftDeviceCodes;
    private readonly SynchronizationContext? _uiContext;
    private readonly object _instancesSync = new();
    private readonly object _minecraftRootFoldersSync = new();
    private readonly object _versionSelectorRowsSync = new();
    private readonly object _javaEntriesSync = new();
    private readonly object _javaEntryOptionsSync = new();
    private readonly object _stepsSync = new();
    private readonly object _fileCompletionDetailsSync = new();
    private readonly object _microsoftAccountsSync = new();
    private readonly List<MinecraftInstance> _allInstances = [];
    private bool _isInitialized;
    private bool _isRestoringJavaSelection;
    private bool _isSyncingJavaOptionSelection;
    private bool _isSyncingRootFolderSelection;
    private bool _isSyncingVersionSelectorRow;
    private bool _isChangingRootPathFromSelection;
    private bool _isApplyingUiUpdate;
    private int _uiUpdateThreadId;
    private string _selectionRecoveryStatusMessage = string.Empty;
    private CancellationTokenSource? _busyCancellation;

    public LaunchPageViewModel(
        IMinecraftDiscoveryService minecraftDiscovery,
        IJavaDiscoveryService javaDiscovery,
        ILaunchPipelineService launchPipeline,
        IAppSettingsService settings,
        IFileDialogService fileDialogs,
        ILegacyLoginService legacyLogin,
        IAppLoggerService logger,
        IMinecraftGameDirectoryService? gameDirectories = null,
        IMinecraftRootFolderService? rootFolders = null,
        IMinecraftSelectionService? selections = null,
        IUserPromptService? prompts = null,
        IUiDispatcherService? dispatcher = null,
        IMinecraftInstanceManagementService? instanceManagement = null,
        IFolderOpenService? folders = null,
        ILoginService? loginService = null,
        IMicrosoftDeviceCodeStatusService? microsoftDeviceCodes = null,
        IJavaSelectorService? javaSelector = null)
        : base(PageRoute.Launch, "启动", "登录、选择实例与 Java，然后按旧版流程启动")
    {
        _minecraftDiscovery = minecraftDiscovery;
        _javaDiscovery = javaDiscovery;
        _javaSelector = javaSelector ?? new JavaSelectorService();
        _launchPipeline = launchPipeline;
        _instanceManagement = instanceManagement ?? new MinecraftInstanceManagementService();
        _settings = settings;
        _fileDialogs = fileDialogs;
        _legacyLogin = legacyLogin;
        _loginService = loginService;
        _logger = logger;
        _gameDirectories = gameDirectories ?? new MinecraftGameDirectoryService(settings);
        _rootFolders = rootFolders ?? new MinecraftRootFolderService(settings);
        _selections = selections ?? new MinecraftSelectionService();
        _prompts = prompts ?? new UserPromptService();
        _dispatcher = dispatcher;
        _folders = folders ?? new FolderOpenService();
        _microsoftDeviceCodes = microsoftDeviceCodes;
        _uiContext = SynchronizationContext.Current;
        EnableCollectionSynchronization();

        var savedRoot = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        minecraftRootPath = string.IsNullOrWhiteSpace(savedRoot) ? _minecraftDiscovery.GetDefaultMinecraftRoot() : savedRoot;
        RefreshMinecraftRootFolders();
        selectedLoginType = _settings.Get(AppSettingKeys.LoginType, LoginType.Legacy);
        microsoftClientId = _settings.Get(AppSettingKeys.MicrosoftClientId, "");
        isMicrosoftClientIdEditorVisible = selectedLoginType == LoginType.Ms && !HasMicrosoftClientId;
        launchSkinType = _settings.Get(AppSettingKeys.LaunchSkinType, 0);
        launchSkinId = _settings.Get(AppSettingKeys.LaunchSkinID, "");
        launchSkinSlim = _settings.Get(AppSettingKeys.LaunchSkinSlim, false);
        legacyName = _settings.Get(AppSettingKeys.LoginLegacyName, "Steve").Split('篓', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Steve";
        loginUserName = GetLoginUserName(selectedLoginType);
        loginPassword = GetLoginPassword(selectedLoginType);
        loginServer = GetLoginServer(selectedLoginType);
        rememberLogin = _settings.Get(AppSettingKeys.LoginRemember, true);
        launcherVisibility = _settings.Get(AppSettingKeys.LaunchArgumentVisible, 5);
        launchWindowWidth = _settings.Get(AppSettingKeys.LaunchArgumentWindowWidth, 854);
        launchWindowHeight = _settings.Get(AppSettingKeys.LaunchArgumentWindowHeight, 480);
        launchWindowType = _settings.Get(AppSettingKeys.LaunchArgumentWindowType, 1);
        versionSortMode = NormalizeVersionSortMode(_settings.Get(AppSettingKeys.VersionSortMode, 0));
        LoadLaunchSettings(instanceName: null);

        RegisterCommands();
        _launchPipeline.StepsChanged += HandleLaunchStepsChanged;
        if (_microsoftDeviceCodes is not null)
        {
            _microsoftDeviceCodes.Changed += HandleMicrosoftDeviceCodeChanged;
            RefreshMicrosoftDeviceCodeStatus();
        }

        RefreshMicrosoftAccounts();
    }
}
