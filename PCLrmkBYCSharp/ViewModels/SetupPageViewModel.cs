using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class SetupPageViewModel : PageViewModelBase
{
    public sealed record IntOption(int Value, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record SetupSectionOption(int Value, string DisplayName, string Description)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record LinkProviderOption(LinkProviderKind Value, string DisplayName, string Description)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record LinkLatencyOption(LinkLatencyMode Value, string DisplayName, string Description)
    {
        public override string ToString() => DisplayName;
    }

    private readonly IAppSettingsService _settings;
    private readonly IAppPathService _paths;
    private readonly IFileDialogService _fileDialogs;
    private readonly IAppLoggerService _logger;
    private readonly IUiThemeService? _theme;

    [ObservableProperty]
    private int selectedSetupSection;

    [ObservableProperty]
    private string theme;

    [ObservableProperty]
    private string language;

    [ObservableProperty]
    private int uiScalePercent;

    [ObservableProperty]
    private bool uiAnimation;

    [ObservableProperty]
    private int uiBackgroundOpacity;

    [ObservableProperty]
    private bool uiCompactSidebar;

    [ObservableProperty]
    private bool uiShowPageHints;

    [ObservableProperty]
    private int uiBackgroundSuit;

    [ObservableProperty]
    private int uiBackgroundBlur;

    [ObservableProperty]
    private bool uiBackgroundColorful;

    [ObservableProperty]
    private int uiMusicVolume;

    [ObservableProperty]
    private bool uiMusicRandom;

    [ObservableProperty]
    private bool uiMusicAuto;

    [ObservableProperty]
    private bool uiMusicStart;

    [ObservableProperty]
    private bool uiMusicStop;

    [ObservableProperty]
    private int uiLogoType;

    [ObservableProperty]
    private bool uiLogoLeft;

    [ObservableProperty]
    private string uiLogoText;

    [ObservableProperty]
    private int uiCustomType;

    [ObservableProperty]
    private string uiCustomNet;

    [ObservableProperty]
    private bool accessibilityLargeText;

    [ObservableProperty]
    private bool accessibilityReducedMotion;

    [ObservableProperty]
    private bool accessibilityHighContrast;

    [ObservableProperty]
    private bool accessibilityKeyboardFocus;

    [ObservableProperty]
    private bool accessibilityConfirmDangerousActions;

    [ObservableProperty]
    private bool toolUpdateRelease;

    [ObservableProperty]
    private bool toolUpdateSnapshot;

    [ObservableProperty]
    private bool toolHelpChinese;

    [ObservableProperty]
    private int systemSystemUpdate;

    [ObservableProperty]
    private int systemSystemActivity;

    [ObservableProperty]
    private string systemSystemCache;

    [ObservableProperty]
    private bool systemSystemTelemetry;

    [ObservableProperty]
    private bool systemDebugMode;

    [ObservableProperty]
    private int systemDebugAnim;

    [ObservableProperty]
    private bool systemDebugSkipCopy;

    [ObservableProperty]
    private bool systemDebugDelay;

    [ObservableProperty]
    private string minecraftRootPath;

    [ObservableProperty]
    private int launchWindowWidth;

    [ObservableProperty]
    private int launchWindowHeight;

    [ObservableProperty]
    private int launchWindowType;

    [ObservableProperty]
    private string launchArgumentJavaSelect;

    [ObservableProperty]
    private int launchArgumentIndieV2;

    [ObservableProperty]
    private int launchArgumentPriority;

    [ObservableProperty]
    private int launchRamType;

    [ObservableProperty]
    private int launchRamCustom;

    [ObservableProperty]
    private bool launchArgumentRam;

    [ObservableProperty]
    private int launchAdvanceGc;

    [ObservableProperty]
    private string launchArgumentTitle;

    [ObservableProperty]
    private string launchArgumentInfo;

    [ObservableProperty]
    private string launchAdvanceJvm;

    [ObservableProperty]
    private string launchAdvanceGame;

    [ObservableProperty]
    private string launchAdvanceRun;

    [ObservableProperty]
    private bool launchAdvanceRunWait;

    [ObservableProperty]
    private bool launchAdvanceDisableJlw;

    [ObservableProperty]
    private bool launchAdvanceDisableLua;

    [ObservableProperty]
    private bool launchAdvanceGraphicCard;

    [ObservableProperty]
    private int launchArgumentVisible;

    [ObservableProperty]
    private int launchSkinType;

    [ObservableProperty]
    private string launchSkinId;

    [ObservableProperty]
    private bool launchSkinSlim;

    [ObservableProperty]
    private int toolDownloadSource;

    [ObservableProperty]
    private int toolDownloadVersion;

    [ObservableProperty]
    private int toolDownloadThread;

    [ObservableProperty]
    private int toolDownloadSpeed;

    [ObservableProperty]
    private bool toolDownloadCert;

    [ObservableProperty]
    private int toolDownloadMod;

    [ObservableProperty]
    private int toolDownloadTranslateV2;

    [ObservableProperty]
    private int toolModLocalNameStyle;

    [ObservableProperty]
    private bool toolDownloadIgnoreQuilt;

    [ObservableProperty]
    private LinkProviderKind linkProvider;

    [ObservableProperty]
    private LinkLatencyMode linkLatencyMode;

    [ObservableProperty]
    private string linkCustomPeer;

    [ObservableProperty]
    private int linkServerPort;

    [ObservableProperty]
    private string linkTerracottaExecutablePath;

    [ObservableProperty]
    private string linkEasyTierExecutablePath;

    [ObservableProperty]
    private string statusMessage = "设置系统已就绪";

    public SetupPageViewModel(
        IAppSettingsService settings,
        IAppPathService paths,
        IFileDialogService fileDialogs,
        IAppLoggerService logger,
        IUiThemeService? uiTheme = null)
        : base(PageRoute.Setup, "设置", "全局配置、界面、Minecraft 路径与系统状态")
    {
        _settings = settings;
        _paths = paths;
        _fileDialogs = fileDialogs;
        _logger = logger;
        _theme = uiTheme;

        theme = _settings.Get(AppSettingKeys.Theme, "VS2022Dark");
        language = _settings.Get(AppSettingKeys.Language, "zh-CN");
        uiScalePercent = _settings.Get(AppSettingKeys.UiScalePercent, 100);
        uiAnimation = _settings.Get(AppSettingKeys.UiAnimation, true);
        uiBackgroundOpacity = _settings.Get(AppSettingKeys.UiBackgroundOpacity, 100);
        uiCompactSidebar = _settings.Get(AppSettingKeys.UiCompactSidebar, false);
        uiShowPageHints = _settings.Get(AppSettingKeys.UiShowPageHints, true);
        uiBackgroundSuit = _settings.Get(AppSettingKeys.UiBackgroundSuit, 0);
        uiBackgroundBlur = _settings.Get(AppSettingKeys.UiBackgroundBlur, 0);
        uiBackgroundColorful = _settings.Get(AppSettingKeys.UiBackgroundColorful, false);
        uiMusicVolume = _settings.Get(AppSettingKeys.UiMusicVolume, 50);
        uiMusicRandom = _settings.Get(AppSettingKeys.UiMusicRandom, true);
        uiMusicAuto = _settings.Get(AppSettingKeys.UiMusicAuto, false);
        uiMusicStart = _settings.Get(AppSettingKeys.UiMusicStart, false);
        uiMusicStop = _settings.Get(AppSettingKeys.UiMusicStop, false);
        uiLogoType = _settings.Get(AppSettingKeys.UiLogoType, 1);
        uiLogoLeft = _settings.Get(AppSettingKeys.UiLogoLeft, true);
        uiLogoText = _settings.Get(AppSettingKeys.UiLogoText, "PCL Sharp");
        uiCustomType = _settings.Get(AppSettingKeys.UiCustomType, 0);
        uiCustomNet = _settings.Get(AppSettingKeys.UiCustomNet, "");
        accessibilityLargeText = _settings.Get(AppSettingKeys.AccessibilityLargeText, false);
        accessibilityReducedMotion = _settings.Get(AppSettingKeys.AccessibilityReducedMotion, false);
        accessibilityHighContrast = _settings.Get(AppSettingKeys.AccessibilityHighContrast, false);
        accessibilityKeyboardFocus = _settings.Get(AppSettingKeys.AccessibilityKeyboardFocus, true);
        accessibilityConfirmDangerousActions = _settings.Get(AppSettingKeys.AccessibilityConfirmDangerousActions, true);
        toolUpdateRelease = _settings.Get(AppSettingKeys.ToolUpdateRelease, true);
        toolUpdateSnapshot = _settings.Get(AppSettingKeys.ToolUpdateSnapshot, false);
        toolHelpChinese = _settings.Get(AppSettingKeys.ToolHelpChinese, true);
        systemSystemUpdate = _settings.Get(AppSettingKeys.SystemSystemUpdate, 1);
        systemSystemActivity = _settings.Get(AppSettingKeys.SystemSystemActivity, 1);
        systemSystemCache = _settings.Get(AppSettingKeys.SystemSystemCache, "");
        systemSystemTelemetry = _settings.Get(AppSettingKeys.SystemSystemTelemetry, false);
        systemDebugMode = _settings.Get(AppSettingKeys.SystemDebugMode, false);
        systemDebugAnim = _settings.Get(AppSettingKeys.SystemDebugAnim, 15);
        systemDebugSkipCopy = _settings.Get(AppSettingKeys.SystemDebugSkipCopy, false);
        systemDebugDelay = _settings.Get(AppSettingKeys.SystemDebugDelay, false);
        minecraftRootPath = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        launchWindowWidth = _settings.Get(AppSettingKeys.LaunchArgumentWindowWidth, 854);
        launchWindowHeight = _settings.Get(AppSettingKeys.LaunchArgumentWindowHeight, 480);
        launchWindowType = _settings.Get(AppSettingKeys.LaunchArgumentWindowType, 1);
        launchArgumentJavaSelect = _settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, "");
        launchArgumentIndieV2 = _settings.Get(AppSettingKeys.LaunchArgumentIndieV2, 4);
        launchArgumentPriority = _settings.Get(AppSettingKeys.LaunchArgumentPriority, 1);
        launchRamType = _settings.Get(AppSettingKeys.LaunchRamType, 0);
        launchRamCustom = _settings.Get(AppSettingKeys.LaunchRamCustom, 15);
        launchArgumentRam = _settings.Get(AppSettingKeys.LaunchArgumentRam, false);
        launchAdvanceGc = _settings.Get(AppSettingKeys.LaunchAdvanceGC, 4);
        launchArgumentTitle = _settings.Get(AppSettingKeys.LaunchArgumentTitle, "");
        launchArgumentInfo = _settings.Get(AppSettingKeys.LaunchArgumentInfo, "PCL");
        launchAdvanceJvm = _settings.Get(AppSettingKeys.LaunchAdvanceJvm, "");
        launchAdvanceGame = _settings.Get(AppSettingKeys.LaunchAdvanceGame, "");
        launchAdvanceRun = _settings.Get(AppSettingKeys.LaunchAdvanceRun, "");
        launchAdvanceRunWait = _settings.Get(AppSettingKeys.LaunchAdvanceRunWait, true);
        launchAdvanceDisableJlw = _settings.Get(AppSettingKeys.LaunchAdvanceDisableJLW, false);
        launchAdvanceDisableLua = _settings.Get(AppSettingKeys.LaunchAdvanceDisableLUA, false);
        launchAdvanceGraphicCard = _settings.Get(AppSettingKeys.LaunchAdvanceGraphicCard, true);
        launchArgumentVisible = _settings.Get(AppSettingKeys.LaunchArgumentVisible, 5);
        launchSkinType = _settings.Get(AppSettingKeys.LaunchSkinType, 0);
        launchSkinId = _settings.Get(AppSettingKeys.LaunchSkinID, "");
        launchSkinSlim = _settings.Get(AppSettingKeys.LaunchSkinSlim, false);
        toolDownloadSource = _settings.Get(AppSettingKeys.ToolDownloadSource, 1);
        toolDownloadVersion = _settings.Get(AppSettingKeys.ToolDownloadVersion, 1);
        toolDownloadThread = _settings.Get(AppSettingKeys.ToolDownloadThread, 63);
        toolDownloadSpeed = _settings.Get(AppSettingKeys.ToolDownloadSpeed, 42);
        toolDownloadCert = _settings.Get(AppSettingKeys.ToolDownloadCert, false);
        toolDownloadMod = _settings.Get(AppSettingKeys.ToolDownloadMod, 2);
        toolDownloadTranslateV2 = _settings.Get(AppSettingKeys.ToolDownloadTranslateV2, 1);
        toolModLocalNameStyle = _settings.Get(AppSettingKeys.ToolModLocalNameStyle, 0);
        toolDownloadIgnoreQuilt = _settings.Get(AppSettingKeys.ToolDownloadIgnoreQuilt, false);
        linkProvider = _settings.Get(AppSettingKeys.LinkProvider, LinkProviderKind.Terracotta);
        linkLatencyMode = _settings.Get(AppSettingKeys.LinkLatencyMode, LinkLatencyMode.DirectFirst);
        linkCustomPeer = _settings.Get(AppSettingKeys.LinkCustomPeer, "");
        linkServerPort = _settings.Get(AppSettingKeys.LinkServerPort, 25565);
        linkTerracottaExecutablePath = _settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, "");
        linkEasyTierExecutablePath = _settings.Get(AppSettingKeys.LinkEasyTierExecutablePath, "");
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ResetLastRouteCommand = new RelayCommand(ResetLastRoute);
        BrowseMinecraftRootCommand = new RelayCommand(BrowseMinecraftRoot);
        BrowseLaunchJavaCommand = new RelayCommand(BrowseLaunchJava);
        BrowseLinkTerracottaExecutableCommand = new RelayCommand(() => BrowseLinkExecutable(LinkProviderKind.Terracotta));
        BrowseLinkEasyTierExecutableCommand = new RelayCommand(() => BrowseLinkExecutable(LinkProviderKind.EasyTier));
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["VS2022Dark"];

    public IReadOnlyList<string> LanguageOptions { get; } = ["zh-CN"];

    public IReadOnlyList<IntOption> UiBackgroundSuitOptions { get; } =
    [
        new(0, "自动"),
        new(4, "平铺"),
        new(1, "居中"),
        new(3, "拉伸"),
        new(2, "居中（保持长宽比）"),
        new(5, "左上（保持长宽比）"),
        new(8, "右下（保持长宽比）")
    ];

    public IReadOnlyList<IntOption> UiLogoTypeOptions { get; } =
    [
        new(0, "无"),
        new(1, "默认"),
        new(2, "文本"),
        new(3, "图片")
    ];

    public IReadOnlyList<IntOption> UiCustomTypeOptions { get; } =
    [
        new(0, "空白"),
        new(3, "预设"),
        new(1, "读取本地文件"),
        new(2, "联网更新")
    ];

    public IReadOnlyList<IntOption> SystemUpdateOptions { get; } =
    [
        new(0, "有新版本时自动下载"),
        new(1, "有新版本时显示提示"),
        new(2, "仅重大漏洞更新时提示"),
        new(3, "关闭更新提示")
    ];

    public IReadOnlyList<IntOption> SystemActivityOptions { get; } =
    [
        new(0, "显示所有公告"),
        new(1, "仅显示重要通知"),
        new(2, "关闭所有公告")
    ];

    public IReadOnlyList<IntOption> LaunchSkinTypeOptions { get; } =
    [
        new(0, "随机"),
        new(1, "Steve"),
        new(2, "Alex"),
        new(3, "正版皮肤"),
        new(4, "自定义")
    ];

    public IReadOnlyList<SetupSectionOption> SetupSections { get; } =
    [
        new(0, "常规", "界面、路径与状态"),
        new(1, "个性化", "外观、动画与页面密度"),
        new(2, "辅助功能", "文字、动画、焦点与确认"),
        new(3, "下载", "下载源与资源管理"),
        new(4, "启动", "Java、窗口、内存与 GC"),
        new(5, "高级", "参数、自定义命令与兼容开关"),
        new(6, "联机", "陶瓦联机、EasyTier 与节点")
    ];

    public bool IsGeneralSectionSelected => SelectedSetupSection == 0;

    public bool IsPersonalizationSectionSelected => SelectedSetupSection == 1;

    public bool IsAccessibilitySectionSelected => SelectedSetupSection == 2;

    public bool IsDownloadSectionSelected => SelectedSetupSection == 3;

    public bool IsLaunchSectionSelected => SelectedSetupSection == 4;

    public bool IsAdvancedSectionSelected => SelectedSetupSection == 5;

    public bool IsLinkSectionSelected => SelectedSetupSection == 6;

    public IReadOnlyList<IntOption> LaunchWindowTypeOptions { get; } =
    [
        new(0, "全屏"),
        new(1, "默认"),
        new(2, "与启动器尺寸一致"),
        new(3, "自定义尺寸"),
        new(4, "最大化")
    ];

    public IReadOnlyList<IntOption> LaunchArgumentIndieOptions { get; } =
    [
        new(0, "关闭"),
        new(1, "隔离可安装 Mod 的版本"),
        new(2, "隔离非正式版"),
        new(3, "隔离可安装 Mod 的版本与非正式版"),
        new(4, "隔离所有版本")
    ];

    public IReadOnlyList<IntOption> LaunchArgumentPriorityOptions { get; } =
    [
        new(0, "高（优先保证游戏运行）"),
        new(1, "中（平衡）"),
        new(2, "低（适合挂机）")
    ];

    public IReadOnlyList<IntOption> LaunchRamTypeOptions { get; } =
    [
        new(0, "自动配置"),
        new(1, "自定义")
    ];

    public IReadOnlyList<IntOption> LaunchAdvanceGcOptions { get; } =
    [
        new(0, "尽量使用 ZGC"),
        new(1, "尽量使用分代 ZGC"),
        new(2, "标准 G1GC"),
        new(4, "调优 G1GC"),
        new(3, "不指定（可自定义）")
    ];

    public IReadOnlyList<IntOption> LaunchArgumentVisibleOptions { get; } =
    [
        new(0, "启动后立即关闭"),
        new(2, "启动后隐藏，退出后自动关闭"),
        new(3, "启动后隐藏，退出后重新打开"),
        new(4, "启动后最小化"),
        new(5, "启动后保持不变")
    ];

    public IReadOnlyList<IntOption> ToolDownloadSourceOptions { get; } =
    [
        new(0, "尽量使用镜像源"),
        new(1, "优先使用官方源，在加载缓慢时换用镜像源"),
        new(2, "尽量使用官方源")
    ];

    public IReadOnlyList<IntOption> ToolDownloadVersionOptions { get; } =
    [
        new(0, "尽量使用镜像源（可能缺少刚刚更新的版本）"),
        new(1, "优先使用官方源，在加载缓慢时换用镜像源"),
        new(2, "尽量使用官方源")
    ];

    public IReadOnlyList<IntOption> ToolDownloadModOptions { get; } =
    [
        new(0, "尽量使用镜像源（可能缺少刚刚更新的版本）"),
        new(1, "仅在官方源加载缓慢时改用镜像源"),
        new(2, "尽量使用官方源")
    ];

    public IReadOnlyList<IntOption> ToolDownloadTranslateOptions { get; } =
    [
        new(0, "【机械动力】create-1.21.1-6.0.4"),
        new(1, "[机械动力] create-1.21.1-6.0.4"),
        new(2, "机械动力-create-1.21.1-6.0.4"),
        new(3, "create-1.21.1-6.0.4-机械动力"),
        new(4, "create-1.21.1-6.0.4")
    ];

    public IReadOnlyList<IntOption> ToolModLocalNameStyleOptions { get; } =
    [
        new(0, "标题显示译名，详情显示文件名"),
        new(1, "标题显示文件名，详情显示译名")
    ];

    public IReadOnlyList<LinkProviderOption> LinkProviderOptions { get; } =
    [
        new(LinkProviderKind.Terracotta, "陶瓦联机 Terracotta", "面向 Minecraft 的联机方案，优先做成原版 PCL 的易用体验。"),
        new(LinkProviderKind.EasyTier, "EasyTier 高级模式", "保留底层组网能力，适合自定义节点和更细的网络配置。")
    ];

    public IReadOnlyList<LinkLatencyOption> LinkLatencyOptions { get; } =
    [
        new(LinkLatencyMode.DirectFirst, "优先直连", "优先尝试点对点直连，失败后再使用中继或备用路径。"),
        new(LinkLatencyMode.LatencyFirst, "优先低延迟", "优先选择延迟更低的节点或路径，适合跨地区联机。")
    ];

    public IRelayCommand SaveSettingsCommand { get; }

    public IRelayCommand ResetLastRouteCommand { get; }

    public IRelayCommand BrowseMinecraftRootCommand { get; }

    public IRelayCommand BrowseLaunchJavaCommand { get; }

    public IRelayCommand BrowseLinkTerracottaExecutableCommand { get; }

    public IRelayCommand BrowseLinkEasyTierExecutableCommand { get; }

    public string SettingsFilePath => _paths.SettingsFilePath;

    public string LastRoute => _settings.Get(AppSettingKeys.LastRoute, PageRoute.Launch).ToString();

    public string WindowSizeText => $"{_settings.Get(AppSettingKeys.WindowWidth, 1040d):0} x {_settings.Get(AppSettingKeys.WindowHeight, 640d):0}";

    public string WindowPositionText => $"{_settings.Get(AppSettingKeys.WindowLeft, double.NaN):0}, {_settings.Get(AppSettingKeys.WindowTop, double.NaN):0}";

    partial void OnSelectedSetupSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralSectionSelected));
        OnPropertyChanged(nameof(IsPersonalizationSectionSelected));
        OnPropertyChanged(nameof(IsAccessibilitySectionSelected));
        OnPropertyChanged(nameof(IsDownloadSectionSelected));
        OnPropertyChanged(nameof(IsLaunchSectionSelected));
        OnPropertyChanged(nameof(IsAdvancedSectionSelected));
        OnPropertyChanged(nameof(IsLinkSectionSelected));
    }

    public override Task OnNavigatedToAsync()
    {
        Theme = _settings.Get(AppSettingKeys.Theme, "VS2022Dark");
        Language = _settings.Get(AppSettingKeys.Language, "zh-CN");
        UiScalePercent = _settings.Get(AppSettingKeys.UiScalePercent, 100);
        UiAnimation = _settings.Get(AppSettingKeys.UiAnimation, true);
        UiBackgroundOpacity = _settings.Get(AppSettingKeys.UiBackgroundOpacity, 100);
        UiCompactSidebar = _settings.Get(AppSettingKeys.UiCompactSidebar, false);
        UiShowPageHints = _settings.Get(AppSettingKeys.UiShowPageHints, true);
        UiBackgroundSuit = _settings.Get(AppSettingKeys.UiBackgroundSuit, 0);
        UiBackgroundBlur = _settings.Get(AppSettingKeys.UiBackgroundBlur, 0);
        UiBackgroundColorful = _settings.Get(AppSettingKeys.UiBackgroundColorful, false);
        UiMusicVolume = _settings.Get(AppSettingKeys.UiMusicVolume, 50);
        UiMusicRandom = _settings.Get(AppSettingKeys.UiMusicRandom, true);
        UiMusicAuto = _settings.Get(AppSettingKeys.UiMusicAuto, false);
        UiMusicStart = _settings.Get(AppSettingKeys.UiMusicStart, false);
        UiMusicStop = _settings.Get(AppSettingKeys.UiMusicStop, false);
        UiLogoType = _settings.Get(AppSettingKeys.UiLogoType, 1);
        UiLogoLeft = _settings.Get(AppSettingKeys.UiLogoLeft, true);
        UiLogoText = _settings.Get(AppSettingKeys.UiLogoText, "PCL Sharp");
        UiCustomType = _settings.Get(AppSettingKeys.UiCustomType, 0);
        UiCustomNet = _settings.Get(AppSettingKeys.UiCustomNet, "");
        AccessibilityLargeText = _settings.Get(AppSettingKeys.AccessibilityLargeText, false);
        AccessibilityReducedMotion = _settings.Get(AppSettingKeys.AccessibilityReducedMotion, false);
        AccessibilityHighContrast = _settings.Get(AppSettingKeys.AccessibilityHighContrast, false);
        AccessibilityKeyboardFocus = _settings.Get(AppSettingKeys.AccessibilityKeyboardFocus, true);
        AccessibilityConfirmDangerousActions = _settings.Get(AppSettingKeys.AccessibilityConfirmDangerousActions, true);
        ToolUpdateRelease = _settings.Get(AppSettingKeys.ToolUpdateRelease, true);
        ToolUpdateSnapshot = _settings.Get(AppSettingKeys.ToolUpdateSnapshot, false);
        ToolHelpChinese = _settings.Get(AppSettingKeys.ToolHelpChinese, true);
        SystemSystemUpdate = _settings.Get(AppSettingKeys.SystemSystemUpdate, 1);
        SystemSystemActivity = _settings.Get(AppSettingKeys.SystemSystemActivity, 1);
        SystemSystemCache = _settings.Get(AppSettingKeys.SystemSystemCache, "");
        SystemSystemTelemetry = _settings.Get(AppSettingKeys.SystemSystemTelemetry, false);
        SystemDebugMode = _settings.Get(AppSettingKeys.SystemDebugMode, false);
        SystemDebugAnim = _settings.Get(AppSettingKeys.SystemDebugAnim, 15);
        SystemDebugSkipCopy = _settings.Get(AppSettingKeys.SystemDebugSkipCopy, false);
        SystemDebugDelay = _settings.Get(AppSettingKeys.SystemDebugDelay, false);
        MinecraftRootPath = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        LaunchArgumentJavaSelect = _settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, "");
        LaunchWindowWidth = _settings.Get(AppSettingKeys.LaunchArgumentWindowWidth, 854);
        LaunchWindowHeight = _settings.Get(AppSettingKeys.LaunchArgumentWindowHeight, 480);
        LaunchWindowType = _settings.Get(AppSettingKeys.LaunchArgumentWindowType, 1);
        LaunchArgumentIndieV2 = _settings.Get(AppSettingKeys.LaunchArgumentIndieV2, 4);
        LaunchArgumentPriority = _settings.Get(AppSettingKeys.LaunchArgumentPriority, 1);
        LaunchRamType = _settings.Get(AppSettingKeys.LaunchRamType, 0);
        LaunchRamCustom = _settings.Get(AppSettingKeys.LaunchRamCustom, 15);
        LaunchArgumentRam = _settings.Get(AppSettingKeys.LaunchArgumentRam, false);
        LaunchAdvanceGc = _settings.Get(AppSettingKeys.LaunchAdvanceGC, 4);
        LaunchArgumentTitle = _settings.Get(AppSettingKeys.LaunchArgumentTitle, "");
        LaunchArgumentInfo = _settings.Get(AppSettingKeys.LaunchArgumentInfo, "PCL");
        LaunchAdvanceJvm = _settings.Get(AppSettingKeys.LaunchAdvanceJvm, "");
        LaunchAdvanceGame = _settings.Get(AppSettingKeys.LaunchAdvanceGame, "");
        LaunchAdvanceRun = _settings.Get(AppSettingKeys.LaunchAdvanceRun, "");
        LaunchAdvanceRunWait = _settings.Get(AppSettingKeys.LaunchAdvanceRunWait, true);
        LaunchAdvanceDisableJlw = _settings.Get(AppSettingKeys.LaunchAdvanceDisableJLW, false);
        LaunchAdvanceDisableLua = _settings.Get(AppSettingKeys.LaunchAdvanceDisableLUA, false);
        LaunchAdvanceGraphicCard = _settings.Get(AppSettingKeys.LaunchAdvanceGraphicCard, true);
        LaunchArgumentVisible = _settings.Get(AppSettingKeys.LaunchArgumentVisible, 5);
        LaunchSkinType = _settings.Get(AppSettingKeys.LaunchSkinType, 0);
        LaunchSkinId = _settings.Get(AppSettingKeys.LaunchSkinID, "");
        LaunchSkinSlim = _settings.Get(AppSettingKeys.LaunchSkinSlim, false);
        ToolDownloadSource = _settings.Get(AppSettingKeys.ToolDownloadSource, 1);
        ToolDownloadVersion = _settings.Get(AppSettingKeys.ToolDownloadVersion, 1);
        ToolDownloadThread = _settings.Get(AppSettingKeys.ToolDownloadThread, 63);
        ToolDownloadSpeed = _settings.Get(AppSettingKeys.ToolDownloadSpeed, 42);
        ToolDownloadCert = _settings.Get(AppSettingKeys.ToolDownloadCert, false);
        ToolDownloadMod = _settings.Get(AppSettingKeys.ToolDownloadMod, 2);
        ToolDownloadTranslateV2 = _settings.Get(AppSettingKeys.ToolDownloadTranslateV2, 1);
        ToolModLocalNameStyle = _settings.Get(AppSettingKeys.ToolModLocalNameStyle, 0);
        ToolDownloadIgnoreQuilt = _settings.Get(AppSettingKeys.ToolDownloadIgnoreQuilt, false);
        LinkProvider = _settings.Get(AppSettingKeys.LinkProvider, LinkProviderKind.Terracotta);
        LinkLatencyMode = _settings.Get(AppSettingKeys.LinkLatencyMode, LinkLatencyMode.DirectFirst);
        LinkCustomPeer = _settings.Get(AppSettingKeys.LinkCustomPeer, "");
        LinkServerPort = _settings.Get(AppSettingKeys.LinkServerPort, 25565);
        LinkTerracottaExecutablePath = _settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, "");
        LinkEasyTierExecutablePath = _settings.Get(AppSettingKeys.LinkEasyTierExecutablePath, "");
        RefreshComputedProperties();
        return Task.CompletedTask;
    }

    private void BrowseMinecraftRoot()
    {
        var selected = _fileDialogs.PickFolder("选择 Minecraft 根目录", MinecraftRootPath);
        if (selected is null)
        {
            return;
        }

        MinecraftRootPath = selected;
        SaveSettings();
    }

    private void BrowseLaunchJava()
    {
        var initialDirectory = File.Exists(LaunchArgumentJavaSelect)
            ? Path.GetDirectoryName(LaunchArgumentJavaSelect) ?? ""
            : "";
        var selected = _fileDialogs.PickJavaExecutable(initialDirectory);
        if (selected is null)
        {
            return;
        }

        LaunchArgumentJavaSelect = selected;
        SaveSettings();
    }

    private void BrowseLinkExecutable(LinkProviderKind provider)
    {
        var currentPath = provider == LinkProviderKind.Terracotta
            ? LinkTerracottaExecutablePath
            : LinkEasyTierExecutablePath;
        var initialDirectory = File.Exists(currentPath)
            ? Path.GetDirectoryName(currentPath) ?? ""
            : "";
        var title = provider == LinkProviderKind.Terracotta
            ? "选择 Terracotta 可执行文件"
            : "选择 EasyTier 可执行文件";
        var selected = _fileDialogs.PickExecutable(title, initialDirectory, "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");
        if (selected is null)
        {
            return;
        }

        if (provider == LinkProviderKind.Terracotta)
        {
            LinkTerracottaExecutablePath = selected;
        }
        else
        {
            LinkEasyTierExecutablePath = selected;
        }

        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings.Set(AppSettingKeys.Theme, Theme);
        _settings.Set(AppSettingKeys.Language, Language);
        _settings.Set(AppSettingKeys.UiScalePercent, Math.Clamp(UiScalePercent, 80, 140));
        _settings.Set(AppSettingKeys.UiAnimation, UiAnimation);
        _settings.Set(AppSettingKeys.UiBackgroundOpacity, Math.Clamp(UiBackgroundOpacity, 40, 100));
        _settings.Set(AppSettingKeys.UiCompactSidebar, UiCompactSidebar);
        _settings.Set(AppSettingKeys.UiShowPageHints, UiShowPageHints);
        _settings.Set(AppSettingKeys.UiBackgroundSuit, UiBackgroundSuit);
        _settings.Set(AppSettingKeys.UiBackgroundBlur, Math.Clamp(UiBackgroundBlur, 0, 40));
        _settings.Set(AppSettingKeys.UiBackgroundColorful, UiBackgroundColorful);
        _settings.Set(AppSettingKeys.UiMusicVolume, Math.Clamp(UiMusicVolume, 0, 100));
        _settings.Set(AppSettingKeys.UiMusicRandom, UiMusicRandom);
        _settings.Set(AppSettingKeys.UiMusicAuto, UiMusicAuto);
        _settings.Set(AppSettingKeys.UiMusicStart, UiMusicStart);
        _settings.Set(AppSettingKeys.UiMusicStop, UiMusicStop);
        _settings.Set(AppSettingKeys.UiLogoType, UiLogoType);
        _settings.Set(AppSettingKeys.UiLogoLeft, UiLogoLeft);
        _settings.Set(AppSettingKeys.UiLogoText, UiLogoText);
        _settings.Set(AppSettingKeys.UiCustomType, UiCustomType);
        _settings.Set(AppSettingKeys.UiCustomNet, UiCustomNet);
        _settings.Set(AppSettingKeys.AccessibilityLargeText, AccessibilityLargeText);
        _settings.Set(AppSettingKeys.AccessibilityReducedMotion, AccessibilityReducedMotion);
        _settings.Set(AppSettingKeys.AccessibilityHighContrast, AccessibilityHighContrast);
        _settings.Set(AppSettingKeys.AccessibilityKeyboardFocus, AccessibilityKeyboardFocus);
        _settings.Set(AppSettingKeys.AccessibilityConfirmDangerousActions, AccessibilityConfirmDangerousActions);
        _settings.Set(AppSettingKeys.ToolUpdateRelease, ToolUpdateRelease);
        _settings.Set(AppSettingKeys.ToolUpdateSnapshot, ToolUpdateSnapshot);
        _settings.Set(AppSettingKeys.ToolHelpChinese, ToolHelpChinese);
        _settings.Set(AppSettingKeys.SystemSystemUpdate, SystemSystemUpdate);
        _settings.Set(AppSettingKeys.SystemSystemActivity, SystemSystemActivity);
        _settings.Set(AppSettingKeys.SystemSystemCache, SystemSystemCache);
        _settings.Set(AppSettingKeys.SystemSystemTelemetry, SystemSystemTelemetry);
        _settings.Set(AppSettingKeys.SystemDebugMode, SystemDebugMode);
        _settings.Set(AppSettingKeys.SystemDebugAnim, Math.Clamp(SystemDebugAnim, 0, 30));
        _settings.Set(AppSettingKeys.SystemDebugSkipCopy, SystemDebugSkipCopy);
        _settings.Set(AppSettingKeys.SystemDebugDelay, SystemDebugDelay);
        _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
        _settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, LaunchArgumentJavaSelect);
        _settings.Set(AppSettingKeys.LaunchArgumentWindowWidth, LaunchWindowWidth);
        _settings.Set(AppSettingKeys.LaunchArgumentWindowHeight, LaunchWindowHeight);
        _settings.Set(AppSettingKeys.LaunchArgumentWindowType, LaunchWindowType);
        _settings.Set(AppSettingKeys.LaunchArgumentIndieV2, LaunchArgumentIndieV2);
        _settings.Set(AppSettingKeys.LaunchArgumentPriority, LaunchArgumentPriority);
        _settings.Set(AppSettingKeys.LaunchRamType, LaunchRamType);
        _settings.Set(AppSettingKeys.LaunchRamCustom, LaunchRamCustom);
        _settings.Set(AppSettingKeys.LaunchArgumentRam, LaunchArgumentRam);
        _settings.Set(AppSettingKeys.LaunchAdvanceGC, LaunchAdvanceGc);
        _settings.Set(AppSettingKeys.LaunchArgumentTitle, LaunchArgumentTitle);
        _settings.Set(AppSettingKeys.LaunchArgumentInfo, LaunchArgumentInfo);
        _settings.Set(AppSettingKeys.LaunchAdvanceJvm, LaunchAdvanceJvm);
        _settings.Set(AppSettingKeys.LaunchAdvanceGame, LaunchAdvanceGame);
        _settings.Set(AppSettingKeys.LaunchAdvanceRun, LaunchAdvanceRun);
        _settings.Set(AppSettingKeys.LaunchAdvanceRunWait, LaunchAdvanceRunWait);
        _settings.Set(AppSettingKeys.LaunchAdvanceDisableJLW, LaunchAdvanceDisableJlw);
        _settings.Set(AppSettingKeys.LaunchAdvanceDisableLUA, LaunchAdvanceDisableLua);
        _settings.Set(AppSettingKeys.LaunchAdvanceGraphicCard, LaunchAdvanceGraphicCard);
        _settings.Set(AppSettingKeys.LaunchArgumentVisible, LaunchArgumentVisible);
        _settings.Set(AppSettingKeys.LaunchSkinType, LaunchSkinType);
        _settings.Set(AppSettingKeys.LaunchSkinID, LaunchSkinId);
        _settings.Set(AppSettingKeys.LaunchSkinSlim, LaunchSkinSlim);
        _settings.Set(AppSettingKeys.ToolDownloadSource, ToolDownloadSource);
        _settings.Set(AppSettingKeys.ToolDownloadVersion, ToolDownloadVersion);
        _settings.Set(AppSettingKeys.ToolDownloadThread, Math.Clamp(ToolDownloadThread, 1, 255));
        _settings.Set(AppSettingKeys.ToolDownloadSpeed, Math.Clamp(ToolDownloadSpeed, 0, 42));
        _settings.Set(AppSettingKeys.ToolDownloadCert, ToolDownloadCert);
        _settings.Set(AppSettingKeys.ToolDownloadMod, ToolDownloadMod);
        _settings.Set(AppSettingKeys.ToolDownloadTranslateV2, ToolDownloadTranslateV2);
        _settings.Set(AppSettingKeys.ToolModLocalNameStyle, ToolModLocalNameStyle);
        _settings.Set(AppSettingKeys.ToolDownloadIgnoreQuilt, ToolDownloadIgnoreQuilt);
        _settings.Set(AppSettingKeys.LinkProvider, LinkProvider);
        _settings.Set(AppSettingKeys.LinkLatencyMode, LinkLatencyMode);
        _settings.Set(AppSettingKeys.LinkCustomPeer, LinkCustomPeer);
        _settings.Set(AppSettingKeys.LinkServerPort, Math.Clamp(LinkServerPort, 1, 65535));
        _settings.Set(AppSettingKeys.LinkTerracottaExecutablePath, LinkTerracottaExecutablePath);
        _settings.Set(AppSettingKeys.LinkEasyTierExecutablePath, LinkEasyTierExecutablePath);
        _settings.SaveAsync().GetAwaiter().GetResult();
        _theme?.Apply(_settings);
        StatusMessage = "设置已保存";
        _logger.Info("设置页保存设置");
        RefreshComputedProperties();
    }

    private void ResetLastRoute()
    {
        _settings.Reset(AppSettingKeys.LastRoute);
        _settings.SaveAsync().GetAwaiter().GetResult();
        StatusMessage = "最近页面已重置";
        _logger.Info("设置页重置最近页面");
        RefreshComputedProperties();
    }

    private void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(LastRoute));
        OnPropertyChanged(nameof(WindowSizeText));
        OnPropertyChanged(nameof(WindowPositionText));
    }
}
