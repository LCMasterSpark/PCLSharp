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

    private readonly IAppSettingsService _settings;
    private readonly IAppPathService _paths;
    private readonly IFileDialogService _fileDialogs;
    private readonly IAppLoggerService _logger;

    [ObservableProperty]
    private int selectedSetupSection;

    [ObservableProperty]
    private string theme;

    [ObservableProperty]
    private string language;

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
    private string statusMessage = "设置系统已就绪";

    public SetupPageViewModel(
        IAppSettingsService settings,
        IAppPathService paths,
        IFileDialogService fileDialogs,
        IAppLoggerService logger)
        : base(PageRoute.Setup, "设置", "全局配置、界面、Minecraft 路径与系统状态")
    {
        _settings = settings;
        _paths = paths;
        _fileDialogs = fileDialogs;
        _logger = logger;

        theme = _settings.Get(AppSettingKeys.Theme, "VS2022Dark");
        language = _settings.Get(AppSettingKeys.Language, "zh-CN");
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
        toolDownloadSource = _settings.Get(AppSettingKeys.ToolDownloadSource, 1);
        toolDownloadVersion = _settings.Get(AppSettingKeys.ToolDownloadVersion, 1);
        toolDownloadThread = _settings.Get(AppSettingKeys.ToolDownloadThread, 63);
        toolDownloadSpeed = _settings.Get(AppSettingKeys.ToolDownloadSpeed, 42);
        toolDownloadCert = _settings.Get(AppSettingKeys.ToolDownloadCert, false);
        toolDownloadMod = _settings.Get(AppSettingKeys.ToolDownloadMod, 2);
        toolDownloadTranslateV2 = _settings.Get(AppSettingKeys.ToolDownloadTranslateV2, 1);
        toolModLocalNameStyle = _settings.Get(AppSettingKeys.ToolModLocalNameStyle, 0);
        toolDownloadIgnoreQuilt = _settings.Get(AppSettingKeys.ToolDownloadIgnoreQuilt, false);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ResetLastRouteCommand = new RelayCommand(ResetLastRoute);
        BrowseMinecraftRootCommand = new RelayCommand(BrowseMinecraftRoot);
        BrowseLaunchJavaCommand = new RelayCommand(BrowseLaunchJava);
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["VS2022Dark"];

    public IReadOnlyList<string> LanguageOptions { get; } = ["zh-CN"];

    public IReadOnlyList<SetupSectionOption> SetupSections { get; } =
    [
        new(0, "常规", "界面、路径与状态"),
        new(1, "下载", "下载源与资源管理"),
        new(2, "启动", "Java、窗口、内存与 GC"),
        new(3, "高级", "参数、自定义命令与兼容开关")
    ];

    public bool IsGeneralSectionSelected => SelectedSetupSection == 0;

    public bool IsDownloadSectionSelected => SelectedSetupSection == 1;

    public bool IsLaunchSectionSelected => SelectedSetupSection == 2;

    public bool IsAdvancedSectionSelected => SelectedSetupSection == 3;

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

    public IRelayCommand SaveSettingsCommand { get; }

    public IRelayCommand ResetLastRouteCommand { get; }

    public IRelayCommand BrowseMinecraftRootCommand { get; }

    public IRelayCommand BrowseLaunchJavaCommand { get; }

    public string SettingsFilePath => _paths.SettingsFilePath;

    public string LastRoute => _settings.Get(AppSettingKeys.LastRoute, PageRoute.Launch).ToString();

    public string WindowSizeText => $"{_settings.Get(AppSettingKeys.WindowWidth, 1040d):0} x {_settings.Get(AppSettingKeys.WindowHeight, 640d):0}";

    public string WindowPositionText => $"{_settings.Get(AppSettingKeys.WindowLeft, double.NaN):0}, {_settings.Get(AppSettingKeys.WindowTop, double.NaN):0}";

    partial void OnSelectedSetupSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralSectionSelected));
        OnPropertyChanged(nameof(IsDownloadSectionSelected));
        OnPropertyChanged(nameof(IsLaunchSectionSelected));
        OnPropertyChanged(nameof(IsAdvancedSectionSelected));
    }

    public override Task OnNavigatedToAsync()
    {
        Theme = _settings.Get(AppSettingKeys.Theme, "VS2022Dark");
        Language = _settings.Get(AppSettingKeys.Language, "zh-CN");
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
        ToolDownloadSource = _settings.Get(AppSettingKeys.ToolDownloadSource, 1);
        ToolDownloadVersion = _settings.Get(AppSettingKeys.ToolDownloadVersion, 1);
        ToolDownloadThread = _settings.Get(AppSettingKeys.ToolDownloadThread, 63);
        ToolDownloadSpeed = _settings.Get(AppSettingKeys.ToolDownloadSpeed, 42);
        ToolDownloadCert = _settings.Get(AppSettingKeys.ToolDownloadCert, false);
        ToolDownloadMod = _settings.Get(AppSettingKeys.ToolDownloadMod, 2);
        ToolDownloadTranslateV2 = _settings.Get(AppSettingKeys.ToolDownloadTranslateV2, 1);
        ToolModLocalNameStyle = _settings.Get(AppSettingKeys.ToolModLocalNameStyle, 0);
        ToolDownloadIgnoreQuilt = _settings.Get(AppSettingKeys.ToolDownloadIgnoreQuilt, false);
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

    private void SaveSettings()
    {
        _settings.Set(AppSettingKeys.Theme, Theme);
        _settings.Set(AppSettingKeys.Language, Language);
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
        _settings.Set(AppSettingKeys.ToolDownloadSource, ToolDownloadSource);
        _settings.Set(AppSettingKeys.ToolDownloadVersion, ToolDownloadVersion);
        _settings.Set(AppSettingKeys.ToolDownloadThread, Math.Clamp(ToolDownloadThread, 1, 255));
        _settings.Set(AppSettingKeys.ToolDownloadSpeed, Math.Clamp(ToolDownloadSpeed, 0, 42));
        _settings.Set(AppSettingKeys.ToolDownloadCert, ToolDownloadCert);
        _settings.Set(AppSettingKeys.ToolDownloadMod, ToolDownloadMod);
        _settings.Set(AppSettingKeys.ToolDownloadTranslateV2, ToolDownloadTranslateV2);
        _settings.Set(AppSettingKeys.ToolModLocalNameStyle, ToolModLocalNameStyle);
        _settings.Set(AppSettingKeys.ToolDownloadIgnoreQuilt, ToolDownloadIgnoreQuilt);
        _settings.SaveAsync().GetAwaiter().GetResult();
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
