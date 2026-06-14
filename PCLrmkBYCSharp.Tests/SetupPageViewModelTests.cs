using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class SetupPageViewModelTests
{
    [Fact]
    public void SetupPageSectionsSwitchContentWithoutUsingTabs()
    {
        using var temp = new TempDirectory();
        var viewModel = new SetupPageViewModel(
            new AppSettingsService(new TestAppPathService(temp.Path)),
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService());

        Assert.Equal([0, 1, 2, 3, 4, 5, 6], viewModel.SetupSections.Select(section => section.Value));
        Assert.True(viewModel.IsGeneralSectionSelected);
        Assert.False(viewModel.IsDownloadSectionSelected);

        viewModel.SelectedSetupSection = 1;

        Assert.False(viewModel.IsGeneralSectionSelected);
        Assert.True(viewModel.IsPersonalizationSectionSelected);
        Assert.False(viewModel.IsAccessibilitySectionSelected);
        Assert.False(viewModel.IsDownloadSectionSelected);

        viewModel.SelectedSetupSection = 2;

        Assert.True(viewModel.IsAccessibilitySectionSelected);
        Assert.False(viewModel.IsPersonalizationSectionSelected);
        Assert.False(viewModel.IsDownloadSectionSelected);

        viewModel.SelectedSetupSection = 3;

        Assert.True(viewModel.IsDownloadSectionSelected);
        Assert.False(viewModel.IsLaunchSectionSelected);

        viewModel.SelectedSetupSection = 4;

        Assert.True(viewModel.IsLaunchSectionSelected);
        Assert.False(viewModel.IsAdvancedSectionSelected);

        viewModel.SelectedSetupSection = 5;

        Assert.True(viewModel.IsAdvancedSectionSelected);
        Assert.False(viewModel.IsLaunchSectionSelected);

        viewModel.SelectedSetupSection = 6;

        Assert.True(viewModel.IsLinkSectionSelected);
        Assert.False(viewModel.IsAdvancedSectionSelected);
    }

    [Fact]
    public void OptionObjectsRenderAsDisplayNamesInComboBoxes()
    {
        var option = new SetupPageViewModel.IntOption(1, "优先使用官方源");
        var section = new SetupPageViewModel.SetupSectionOption(2, "启动", "Java、窗口、内存与 GC");
        var provider = new SetupPageViewModel.LinkProviderOption(LinkProviderKind.Terracotta, "陶瓦联机", "默认联机方案");
        var latency = new SetupPageViewModel.LinkLatencyOption(LinkLatencyMode.DirectFirst, "优先直连", "默认路径策略");

        Assert.Equal(option.DisplayName, option.ToString());
        Assert.Equal(section.DisplayName, section.ToString());
        Assert.Equal(provider.DisplayName, provider.ToString());
        Assert.Equal(latency.DisplayName, latency.ToString());
        Assert.DoesNotContain(nameof(SetupPageViewModel.IntOption), option.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(LinkProviderKind), provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveSettingsPersistsLaunchArgumentPriority()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService())
        {
            LaunchArgumentIndieV2 = 3,
            LaunchArgumentPriority = 2,
            LaunchAdvanceGraphicCard = false,
            LaunchArgumentTitle = "自定义 {name}",
            UiScalePercent = 135,
            UiBackgroundOpacity = 55,
            UiAnimation = false,
            AccessibilityLargeText = true,
            AccessibilityConfirmDangerousActions = false
        };

        viewModel.LaunchArgumentInfo = "PCL Sharp {version}";
        viewModel.LaunchRamType = 1;
        viewModel.LaunchRamCustom = 25;
        viewModel.LaunchArgumentRam = true;

        Assert.Equal([0, 1, 2, 3, 4], viewModel.LaunchWindowTypeOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2, 3, 4], viewModel.LaunchArgumentIndieOptions.Select(option => option.Value));
        Assert.Contains(viewModel.LaunchArgumentIndieOptions, option => option.DisplayName.Contains("Mod", StringComparison.Ordinal));
        Assert.Equal([0, 1, 2], viewModel.LaunchArgumentPriorityOptions.Select(option => option.Value));
        Assert.Equal([0, 1], viewModel.LaunchRamTypeOptions.Select(option => option.Value));
        Assert.Equal(["高（优先保证游戏运行）", "中（平衡）", "低（适合挂机）"], viewModel.LaunchArgumentPriorityOptions.Select(option => option.DisplayName));
        Assert.Equal([0, 1, 2, 4, 3], viewModel.LaunchAdvanceGcOptions.Select(option => option.Value));
        Assert.Equal([0, 2, 3, 4, 5], viewModel.LaunchArgumentVisibleOptions.Select(option => option.Value));
        viewModel.SaveSettingsCommand.Execute(null);

        Assert.Equal(3, settings.Get(AppSettingKeys.LaunchArgumentIndieV2, 4));
        Assert.Equal(2, settings.Get(AppSettingKeys.LaunchArgumentPriority, 1));
        Assert.Equal(1, settings.Get(AppSettingKeys.LaunchRamType, 0));
        Assert.Equal(25, settings.Get(AppSettingKeys.LaunchRamCustom, 15));
        Assert.True(settings.Get(AppSettingKeys.LaunchArgumentRam, false));
        Assert.Equal("PCL Sharp {version}", settings.Get(AppSettingKeys.LaunchArgumentInfo, ""));
        Assert.False(settings.Get(AppSettingKeys.LaunchAdvanceGraphicCard, true));
        Assert.Equal("自定义 {name}", settings.Get(AppSettingKeys.LaunchArgumentTitle, ""));
        Assert.Equal(135, settings.Get(AppSettingKeys.UiScalePercent, 100));
        Assert.Equal(55, settings.Get(AppSettingKeys.UiBackgroundOpacity, 100));
        Assert.False(settings.Get(AppSettingKeys.UiAnimation, true));
        Assert.True(settings.Get(AppSettingKeys.AccessibilityLargeText, false));
        Assert.False(settings.Get(AppSettingKeys.AccessibilityConfirmDangerousActions, true));
    }

    [Fact]
    public void SaveSettingsPersistsPersonalizationAccessibilityAndSkinSettings()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService())
        {
            UiBackgroundSuit = 2,
            UiBackgroundBlur = 99,
            UiBackgroundColorful = true,
            UiMusicVolume = -8,
            UiMusicRandom = false,
            UiMusicAuto = true,
            UiLogoType = 2,
            UiLogoLeft = false,
            UiLogoText = "PCL# Preview",
            UiCustomType = 2,
            UiCustomNet = "https://example.invalid/home.xaml",
            ToolUpdateRelease = false,
            ToolUpdateSnapshot = true,
            ToolHelpChinese = false,
            SystemSystemUpdate = 3,
            SystemSystemActivity = 2,
            SystemSystemCache = "D:\\Cache",
            SystemSystemTelemetry = true,
            SystemDebugMode = true,
            SystemDebugAnim = 99,
            SystemDebugSkipCopy = true,
            SystemDebugDelay = true,
            LaunchSkinType = 3,
            LaunchSkinId = "Steve",
            LaunchSkinSlim = true
        };

        Assert.Contains(viewModel.UiBackgroundSuitOptions, option => option.DisplayName.Contains("保持长宽比", StringComparison.Ordinal));
        Assert.Equal([0, 1, 2, 3], viewModel.UiLogoTypeOptions.Select(option => option.Value));
        Assert.Equal([0, 3, 1, 2], viewModel.UiCustomTypeOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2, 3], viewModel.SystemUpdateOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2], viewModel.SystemActivityOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2, 3, 4], viewModel.LaunchSkinTypeOptions.Select(option => option.Value));

        viewModel.SaveSettingsCommand.Execute(null);

        Assert.Equal(2, settings.Get(AppSettingKeys.UiBackgroundSuit, 0));
        Assert.Equal(40, settings.Get(AppSettingKeys.UiBackgroundBlur, 0));
        Assert.True(settings.Get(AppSettingKeys.UiBackgroundColorful, false));
        Assert.Equal(0, settings.Get(AppSettingKeys.UiMusicVolume, 50));
        Assert.False(settings.Get(AppSettingKeys.UiMusicRandom, true));
        Assert.True(settings.Get(AppSettingKeys.UiMusicAuto, false));
        Assert.Equal(2, settings.Get(AppSettingKeys.UiLogoType, 1));
        Assert.False(settings.Get(AppSettingKeys.UiLogoLeft, true));
        Assert.Equal("PCL# Preview", settings.Get(AppSettingKeys.UiLogoText, ""));
        Assert.Equal(2, settings.Get(AppSettingKeys.UiCustomType, 0));
        Assert.Equal("https://example.invalid/home.xaml", settings.Get(AppSettingKeys.UiCustomNet, ""));
        Assert.False(settings.Get(AppSettingKeys.ToolUpdateRelease, true));
        Assert.True(settings.Get(AppSettingKeys.ToolUpdateSnapshot, false));
        Assert.False(settings.Get(AppSettingKeys.ToolHelpChinese, true));
        Assert.Equal(3, settings.Get(AppSettingKeys.SystemSystemUpdate, 1));
        Assert.Equal(2, settings.Get(AppSettingKeys.SystemSystemActivity, 1));
        Assert.Equal("D:\\Cache", settings.Get(AppSettingKeys.SystemSystemCache, ""));
        Assert.True(settings.Get(AppSettingKeys.SystemSystemTelemetry, false));
        Assert.True(settings.Get(AppSettingKeys.SystemDebugMode, false));
        Assert.Equal(30, settings.Get(AppSettingKeys.SystemDebugAnim, 15));
        Assert.True(settings.Get(AppSettingKeys.SystemDebugSkipCopy, false));
        Assert.True(settings.Get(AppSettingKeys.SystemDebugDelay, false));
        Assert.Equal(3, settings.Get(AppSettingKeys.LaunchSkinType, 0));
        Assert.Equal("Steve", settings.Get(AppSettingKeys.LaunchSkinID, ""));
        Assert.True(settings.Get(AppSettingKeys.LaunchSkinSlim, false));
    }

    [Fact]
    public void SaveSettingsAppliesThemeImmediately()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var theme = new CaptureThemeService();
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService(),
            theme)
        {
            UiScalePercent = 120,
            AccessibilityHighContrast = true
        };

        viewModel.SaveSettingsCommand.Execute(null);

        Assert.Equal(1, theme.ApplyCount);
        Assert.Same(settings, theme.LastSettings);
        Assert.Equal(120, settings.Get(AppSettingKeys.UiScalePercent, 100));
        Assert.True(settings.Get(AppSettingKeys.AccessibilityHighContrast, false));
    }

    [Fact]
    public void SaveSettingsPersistsOldPclDownloadSettings()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService())
        {
            ToolDownloadSource = 2,
            ToolDownloadVersion = 0,
            ToolDownloadThread = 512,
            ToolDownloadSpeed = -2,
            ToolDownloadCert = true
        };

        Assert.Equal([0, 1, 2], viewModel.ToolDownloadSourceOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2], viewModel.ToolDownloadVersionOptions.Select(option => option.Value));
        Assert.Contains(viewModel.ToolDownloadVersionOptions, option => option.DisplayName.Contains("镜像源", StringComparison.Ordinal));
        viewModel.SaveSettingsCommand.Execute(null);

        Assert.Equal(2, settings.Get(AppSettingKeys.ToolDownloadSource, 1));
        Assert.Equal(0, settings.Get(AppSettingKeys.ToolDownloadVersion, 1));
        Assert.Equal(255, settings.Get(AppSettingKeys.ToolDownloadThread, 63));
        Assert.Equal(0, settings.Get(AppSettingKeys.ToolDownloadSpeed, 42));
        Assert.True(settings.Get(AppSettingKeys.ToolDownloadCert, false));
    }

    [Fact]
    public void SaveSettingsPersistsOldPclCommunityResourceSettings()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService())
        {
            ToolDownloadMod = 0,
            ToolDownloadTranslateV2 = 3,
            ToolModLocalNameStyle = 1,
            ToolDownloadIgnoreQuilt = true
        };

        Assert.Equal([0, 1, 2], viewModel.ToolDownloadModOptions.Select(option => option.Value));
        Assert.Equal([0, 1, 2, 3, 4], viewModel.ToolDownloadTranslateOptions.Select(option => option.Value));
        Assert.Equal([0, 1], viewModel.ToolModLocalNameStyleOptions.Select(option => option.Value));
        viewModel.SaveSettingsCommand.Execute(null);

        Assert.Equal(0, settings.Get(AppSettingKeys.ToolDownloadMod, 2));
        Assert.Equal(3, settings.Get(AppSettingKeys.ToolDownloadTranslateV2, 1));
        Assert.Equal(1, settings.Get(AppSettingKeys.ToolModLocalNameStyle, 0));
        Assert.True(settings.Get(AppSettingKeys.ToolDownloadIgnoreQuilt, false));
    }

    [Fact]
    public void SaveSettingsPersistsLinkSettings()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService())
        {
            LinkProvider = LinkProviderKind.EasyTier,
            LinkLatencyMode = LinkLatencyMode.LatencyFirst,
            LinkCustomPeer = "tcp://peer.example:11010",
            LinkServerPort = 70000,
            LinkTerracottaExecutablePath = "D:\\Tools\\terracotta.exe",
            LinkEasyTierExecutablePath = "D:\\Tools\\easytier-core.exe"
        };

        Assert.Equal([LinkProviderKind.Terracotta, LinkProviderKind.EasyTier], viewModel.LinkProviderOptions.Select(option => option.Value));
        Assert.Equal([LinkLatencyMode.DirectFirst, LinkLatencyMode.LatencyFirst], viewModel.LinkLatencyOptions.Select(option => option.Value));

        viewModel.SaveSettingsCommand.Execute(null);

        Assert.Equal(LinkProviderKind.EasyTier, settings.Get(AppSettingKeys.LinkProvider, LinkProviderKind.Terracotta));
        Assert.Equal(LinkLatencyMode.LatencyFirst, settings.Get(AppSettingKeys.LinkLatencyMode, LinkLatencyMode.DirectFirst));
        Assert.Equal("tcp://peer.example:11010", settings.Get(AppSettingKeys.LinkCustomPeer, ""));
        Assert.Equal(65535, settings.Get(AppSettingKeys.LinkServerPort, 25565));
        Assert.Equal("D:\\Tools\\terracotta.exe", settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, ""));
        Assert.Equal("D:\\Tools\\easytier-core.exe", settings.Get(AppSettingKeys.LinkEasyTierExecutablePath, ""));
    }

    [Fact]
    public void BrowseLinkExecutablesPersistBackendPaths()
    {
        using var temp = new TempDirectory();
        var terracottaPath = Path.Combine(temp.Path, "Terracotta", "terracotta.exe");
        var easyTierPath = Path.Combine(temp.Path, "EasyTier", "easytier-core.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(terracottaPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(easyTierPath)!);
        File.WriteAllText(terracottaPath, "");
        File.WriteAllText(easyTierPath, "");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var fileDialogs = new LinkExecutableDialogService(terracottaPath, easyTierPath);
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            fileDialogs,
            new NullLoggerService());

        viewModel.BrowseLinkTerracottaExecutableCommand.Execute(null);
        viewModel.BrowseLinkEasyTierExecutableCommand.Execute(null);

        Assert.Equal(terracottaPath, viewModel.LinkTerracottaExecutablePath);
        Assert.Equal(easyTierPath, viewModel.LinkEasyTierExecutablePath);
        Assert.Equal(terracottaPath, settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, ""));
        Assert.Equal(easyTierPath, settings.Get(AppSettingKeys.LinkEasyTierExecutablePath, ""));
        Assert.Equal(["选择 Terracotta 可执行文件", "选择 EasyTier 可执行文件"], fileDialogs.Titles);
    }

    [Fact]
    public void BrowseLaunchJavaPersistsGlobalJavaPath()
    {
        using var temp = new TempDirectory();
        var javaPath = Path.Combine(temp.Path, "java", "bin", "java.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(javaPath)!);
        File.WriteAllText(javaPath, "");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var fileDialogs = new JavaFileDialogService(javaPath);
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            fileDialogs,
            new NullLoggerService());

        viewModel.BrowseLaunchJavaCommand.Execute(null);

        Assert.Equal(javaPath, viewModel.LaunchArgumentJavaSelect);
        Assert.Equal(javaPath, settings.Get(AppSettingKeys.LaunchArgumentJavaSelect, ""));
        Assert.Equal("", fileDialogs.LastInitialDirectory);
    }

    [Fact]
    public async Task OnNavigatedToAsyncReloadsSettingsChangedByOtherPages()
    {
        using var temp = new TempDirectory();
        var firstRoot = Path.Combine(temp.Path, "First");
        var secondRoot = Path.Combine(temp.Path, "Second");
        var firstJava = Path.Combine(temp.Path, "java8", "bin", "java.exe");
        var secondJava = Path.Combine(temp.Path, "java17", "bin", "java.exe");
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.MinecraftRootPath, firstRoot);
        settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, firstJava);
        settings.Set(AppSettingKeys.LaunchArgumentPriority, 0);
        settings.Set(AppSettingKeys.ToolDownloadThread, 16);
        var viewModel = new SetupPageViewModel(
            settings,
            new TestAppPathService(temp.Path),
            new NullFileDialogService(),
            new NullLoggerService());

        settings.Set(AppSettingKeys.MinecraftRootPath, secondRoot);
        settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, secondJava);
        settings.Set(AppSettingKeys.LaunchArgumentPriority, 2);
        settings.Set(AppSettingKeys.ToolDownloadThread, 96);
        await viewModel.OnNavigatedToAsync();

        Assert.Equal(secondRoot, viewModel.MinecraftRootPath);
        Assert.Equal(secondJava, viewModel.LaunchArgumentJavaSelect);
        Assert.Equal(2, viewModel.LaunchArgumentPriority);
        Assert.Equal(96, viewModel.ToolDownloadThread);
    }

    private sealed class JavaFileDialogService(string javaPath) : IFileDialogService
    {
        public string? LastInitialDirectory { get; private set; }

        public string? PickFolder(string title, string initialDirectory)
        {
            return null;
        }

        public string? PickJavaExecutable(string initialDirectory)
        {
            LastInitialDirectory = initialDirectory;
            return javaPath;
        }

        public string? PickSkinFile(string initialDirectory)
        {
            return null;
        }

        public string? PickModpackFile(string initialDirectory)
        {
            return null;
        }

        public IReadOnlyList<string> PickModFiles(string initialDirectory)
        {
            return [];
        }

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter)
        {
            return null;
        }
    }

    private sealed class CaptureThemeService : IUiThemeService
    {
        public int ApplyCount { get; private set; }

        public IAppSettingsService? LastSettings { get; private set; }

        public void Apply(IAppSettingsService settings)
        {
            ApplyCount++;
            LastSettings = settings;
        }

        public void Apply(System.Windows.ResourceDictionary resources, IAppSettingsService settings)
        {
            Apply(settings);
        }
    }

    private sealed class LinkExecutableDialogService(string terracottaPath, string easyTierPath) : IFileDialogService
    {
        private int index;

        public List<string> Titles { get; } = [];

        public string? PickFolder(string title, string initialDirectory)
        {
            return null;
        }

        public string? PickJavaExecutable(string initialDirectory)
        {
            return null;
        }

        public string? PickExecutable(string title, string initialDirectory, string filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*")
        {
            Titles.Add(title);
            return index++ == 0 ? terracottaPath : easyTierPath;
        }

        public string? PickSkinFile(string initialDirectory)
        {
            return null;
        }

        public string? PickModpackFile(string initialDirectory)
        {
            return null;
        }

        public IReadOnlyList<string> PickModFiles(string initialDirectory)
        {
            return [];
        }

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter)
        {
            return null;
        }
    }
}
