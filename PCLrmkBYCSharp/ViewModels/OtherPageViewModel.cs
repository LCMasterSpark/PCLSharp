using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.FeatureHub;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class OtherPageViewModel : PageViewModelBase
{
    public sealed record OtherSectionOption(int Value, string DisplayName, string Description)
    {
        public override string ToString() => DisplayName;
    }

    private readonly IHelpService? _help;
    private readonly IAppLoggerService? _logger;
    private readonly IHelpActionService? _helpActions;
    private readonly IAppPathService? _paths;
    private readonly IAppSettingsService? _settings;
    private readonly IFileDialogService? _fileDialogs;
    private readonly IDownloadManagerService? _downloadManager;
    private readonly ILaunchMemoryOptimizer? _memoryOptimizer;
    private readonly IUserPromptService? _prompts;
    private readonly IAppUpdateCheckService? _updateCheck;
    private readonly IFeatureHubService? _featureHub;
    private readonly IFolderOpenService? _folders;
    private readonly IClipboardService? _clipboard;
    private int _doNotClickCount;
    private IReadOnlyList<HelpEntry> _allHelpEntries = [];

    [ObservableProperty]
    private string helpSearchText = "";

    [ObservableProperty]
    private string helpStatusText = "正在准备帮助索引";

    [ObservableProperty]
    private IReadOnlyList<HelpEntry> helpResults = [];

    [ObservableProperty]
    private HelpEntry? selectedHelpEntry;

    [ObservableProperty]
    private IReadOnlyList<HelpDocumentBlock> selectedHelpDocumentBlocks = [];

    [ObservableProperty]
    private string helpActionStatusText = "";

    [ObservableProperty]
    private string aboutActionStatusText = "";

    [ObservableProperty]
    private string diagnosticsStatusText = "诊断工具已就绪";

    [ObservableProperty]
    private int selectedOtherSection;

    [ObservableProperty]
    private string toolboxStatusText = "百宝箱已就绪";

    [ObservableProperty]
    private string echoCaveText = "反复点击这里可以查看 PCL Sharp 开发与重构过程中的留言。";

    [ObservableProperty]
    private string customDownloadUrl = "";

    [ObservableProperty]
    private string customDownloadFolder = "";

    [ObservableProperty]
    private string customDownloadFileName = "";

    [ObservableProperty]
    private string customDownloadStatusText = "填写下载地址后即可创建下载任务。";

    [ObservableProperty]
    private string featureHubStatusText = "实验与规划入口已就绪";

    [ObservableProperty]
    private string updateStatusText = "更新系统已预留，可手动检查 GitHub Release。";

    [ObservableProperty]
    private string crashAnalysisText = "崩溃分析服务尚未刷新。";

    [ObservableProperty]
    private string accountCenterText = "账号管理中心入口尚未刷新。";

    [ObservableProperty]
    private string skinCenterText = "皮肤中心入口尚未刷新。";

    [ObservableProperty]
    private string extensionPointText = "扩展点目录尚未刷新。";

    [ObservableProperty]
    private IReadOnlyList<FeatureModuleSnapshot> featureModules = [];

    [ObservableProperty]
    private IReadOnlyList<HomeFeedItem> homeFeedItems = [];

    [ObservableProperty]
    private IReadOnlyList<ExtensionPointInfo> extensionPoints = [];

    public OtherPageViewModel(
        IAppPathService? paths = null,
        IHelpService? help = null,
        IAppLoggerService? logger = null,
        IHelpActionService? helpActions = null,
        IAppSettingsService? settings = null,
        IFileDialogService? fileDialogs = null,
        IDownloadManagerService? downloadManager = null,
        ILaunchMemoryOptimizer? memoryOptimizer = null,
        IUserPromptService? prompts = null,
        IAppUpdateCheckService? updateCheck = null,
        IFeatureHubService? featureHub = null,
        IFolderOpenService? folders = null,
        IClipboardService? clipboard = null)
        : base(PageRoute.Other, "更多", "帮助、关于、诊断、反馈与维护工具")
    {
        _help = help;
        _logger = logger;
        _helpActions = helpActions;
        _paths = paths;
        _settings = settings;
        _fileDialogs = fileDialogs;
        _downloadManager = downloadManager;
        _memoryOptimizer = memoryOptimizer;
        _prompts = prompts;
        _updateCheck = updateCheck;
        _featureHub = featureHub;
        _folders = folders;
        _clipboard = clipboard;
        RegisterHelpEventHandlers();
        var assembly = typeof(OtherPageViewModel).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "未知";
        var logPath = paths?.LogsDirectory ?? "日志目录将在应用启动后创建";
        var settingsPath = paths?.SettingsFilePath ?? "设置文件将在应用启动后创建";

        Cards =
        [
            new("帮助系统", "读取内置 Help.zip，支持按标题、描述、关键词与分类搜索。"),
            new("诊断工具", $"日志目录：{logPath}\n设置文件：{settingsPath}"),
            new("运行环境", $".NET：{Environment.Version}\n系统：{Environment.OSVersion.VersionString}\n架构：{RuntimeInformation.ProcessArchitecture}"),
            new("关于页面", $"Plain Craft Launcher Sharp（PCL Sharp / PCL#）\n程序集版本：{version}\n目标：功能一比一还原，交互尽量贴近原版。")
        ];
        AboutLinks =
        [
            new("项目", "Plain Craft Launcher Sharp", $"简称：PCL Sharp / PCL#\n程序集版本：{version}\n定位：实验性 C# WPF 重构版，仍在补齐原版功能。", null),
            new("项目", "重构说明", "目标是功能一比一还原原版 PCL，同时用 MVVM、服务拆分和测试体系降低维护成本。", null),
            new("版权", "原版 PCL", "Plain Craft Launcher 与相关素材、设计和行为参考归原项目及其贡献者所有。", "https://github.com/Hex-Dragon/PCL2"),
            new("版权", "PCL Sharp", "本项目不是原版 PCL 的正式替代品，目前只适合作为实验版与重构预览。", null),
            new("致谢", "龙腾猫跃", "感谢原版 Plain Craft Launcher 的长期开发与公开项目参考。", "https://github.com/Hex-Dragon/PCL2"),
            new("致谢", "bangbang93", "BMCLAPI 镜像源和 Forge 安装工具等生态支持。", "https://bmclapi.bangbang93.com"),
            new("致谢", "MC 百科", "Mod 中文名称、资料索引与社区信息参考。", "https://www.mcmod.cn"),
            new("致谢", "MCIM", "社区资源镜像源和帮助库相关资料参考。", "https://github.com/mcmod-info-mirror"),
            new("致谢", "EasyTier", "后续联机模块与网络体验的参考方向。", "https://github.com/EasyTier/EasyTier"),
            new("致谢", "00ll00", "Java Launch Wrapper 和启动相关服务经验参考。", null),
            new("致谢", "Patrick", "感谢原版 PCL 图标设计带来的视觉识别基础。", null),
            new("致谢", "测试与反馈", "感谢参与 PCL Sharp 试用、截图标注和问题复现的所有人。", null)
        ];
        CustomDownloadFolder = Path.Combine(paths?.AppDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MyDownload");
        RefreshFeatureHub();
    }

    private void RegisterHelpEventHandlers()
    {
        if (_helpActions is null)
        {
            return;
        }

        _helpActions.SetEventHandler(HelpActionService.EventRefreshHelp, async (_, _) =>
        {
            await RefreshHelpAsync();
            return new HelpActionResult(true, "已刷新帮助列表");
        });
        _helpActions.SetEventHandler(HelpActionService.EventRefreshPage, async (_, _) =>
        {
            await RefreshHelpAsync();
            return new HelpActionResult(true, "已刷新当前页面");
        });
        _helpActions.SetEventHandler(HelpActionService.EventOpenHelp, async (eventData, _) =>
        {
            await EnsureHelpEntriesLoadedAsync();
            var entry = FindHelpEntry(eventData);
            if (entry is null)
            {
                return new HelpActionResult(false, string.IsNullOrWhiteSpace(eventData)
                    ? "未指定要打开的帮助条目"
                    : "未找到帮助条目：" + eventData);
            }

            SelectedHelpEntry = entry;
            return new HelpActionResult(true, "已打开帮助：" + entry.Title);
        });
    }

    public IReadOnlyList<PageStatusCard> Cards { get; }

    public IReadOnlyList<PageStatusCard> ToolBoxItems { get; } =
    [
        new("今日人品", "按日期与当前用户生成稳定数值，和原版一样是轻量娱乐入口。"),
        new("内存优化", "尽力清理当前系统进程工作集，并触发 .NET GC。"),
        new("清理游戏垃圾", "清理临时 natives、压缩旧日志与临时下载残留，不触碰存档和版本主体。"),
        new("下载自定义文件", "使用 PCL Sharp 下载队列下载任意直链文件。")
    ];

    public IReadOnlyList<PageStatusCard> FeedbackItems { get; } =
    [
        new("问题反馈", "记录 PCL Sharp 自身的问题，请尽量附带日志、复现步骤与实例信息。"),
        new("原版差异", "发现与原版 PCL 行为不一致时，优先按功能影响程度排入重构任务。"),
        new("体验建议", "侧边栏、分页、按钮位置和列表密度会持续按实际截图调整。")
    ];

    public IReadOnlyList<AboutLink> AboutLinks { get; }

    public IReadOnlyList<OtherSectionOption> OtherSections { get; } =
    [
        new(0, "概览", "帮助、诊断、工具与社区"),
        new(1, "帮助", "搜索并打开内置帮助条目"),
        new(2, "百宝箱", "诊断、维护与快捷工具"),
        new(3, "反馈", "问题反馈与重构建议"),
        new(4, "实验与规划", "更新、崩溃、账号、皮肤与扩展"),
        new(5, "关于与鸣谢", "项目说明、版权与感谢名单")
    ];

    public bool IsOverviewSectionSelected => SelectedOtherSection == 0;

    public bool IsHelpSectionSelected => SelectedOtherSection == 1;

    public bool IsToolBoxSectionSelected => SelectedOtherSection == 2;

    public bool IsFeedbackSectionSelected => SelectedOtherSection == 3;

    public bool IsFeatureHubSectionSelected => SelectedOtherSection == 4;

    public bool IsAboutSectionSelected => SelectedOtherSection == 5;

    public string SelectedHelpPreview
    {
        get
        {
            if (SelectedHelpEntry is null)
            {
                return "选择一个帮助条目以查看摘要。";
            }

            var lines = new List<string>
            {
                SelectedHelpEntry.Title,
                SelectedHelpEntry.Description,
                "分类：" + SelectedHelpEntry.TypeText,
                "路径：" + SelectedHelpEntry.RawPath
            };
            if (SelectedHelpEntry.IsEvent)
            {
                lines.Add("事件：" + SelectedHelpEntry.EventType + " " + SelectedHelpEntry.EventData);
            }
            else
            {
                var readableText = HelpTextExtractor.Extract(SelectedHelpEntry.XamlContent, 40);
                lines.Add(string.IsNullOrWhiteSpace(readableText)
                    ? "内容长度：" + SelectedHelpEntry.XamlContent.Length + " 字符"
                    : readableText);
            }

            return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        if (_paths is null || _folders is null)
        {
            DiagnosticsStatusText = "日志目录服务未初始化";
            return;
        }

        OpenDiagnosticsFolder(_paths.LogsDirectory, "日志目录");
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        if (_paths is null || _folders is null)
        {
            DiagnosticsStatusText = "设置目录服务未初始化";
            return;
        }

        var directory = Path.GetDirectoryName(_paths.SettingsFilePath) ?? _paths.AppDataDirectory;
        OpenDiagnosticsFolder(directory, "设置目录");
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        if (_clipboard is null)
        {
            DiagnosticsStatusText = "剪贴板服务未初始化";
            return;
        }

        var assembly = typeof(OtherPageViewModel).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "未知";
        var lines = new[]
        {
            "Plain Craft Launcher Sharp 诊断信息",
            "版本：" + version,
            ".NET：" + Environment.Version,
            "系统：" + Environment.OSVersion.VersionString,
            "架构：" + RuntimeInformation.ProcessArchitecture,
            "日志目录：" + (_paths?.LogsDirectory ?? "未初始化"),
            "设置文件：" + (_paths?.SettingsFilePath ?? "未初始化"),
            "Minecraft 根目录：" + GetMinecraftRootPath()
        };

        _clipboard.SetText(string.Join(Environment.NewLine, lines));
        DiagnosticsStatusText = "诊断信息已复制到剪贴板";
    }

    private void OpenDiagnosticsFolder(string folderPath, string displayName)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            _folders!.OpenFolder(folderPath);
            DiagnosticsStatusText = "已打开" + displayName + "：" + folderPath;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "打开" + displayName + "失败");
            DiagnosticsStatusText = "打开" + displayName + "失败：" + ex.Message;
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        if (_help is null || _allHelpEntries.Count > 0)
        {
            return;
        }

        try
        {
            HelpStatusText = "正在加载帮助列表";
            _allHelpEntries = await _help.LoadAsync();
            ApplyHelpSearch();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "加载帮助列表失败");
            HelpStatusText = "帮助列表加载失败：" + ex.Message;
        }
    }

    private Task EnsureHelpEntriesLoadedAsync()
    {
        return _allHelpEntries.Count == 0 ? OnNavigatedToAsync() : Task.CompletedTask;
    }

    partial void OnHelpSearchTextChanged(string value)
    {
        ApplyHelpSearch();
    }

    partial void OnSelectedHelpEntryChanged(HelpEntry? value)
    {
        SelectedHelpDocumentBlocks = HelpDocumentParser.Parse(value);
        OnPropertyChanged(nameof(SelectedHelpPreview));
        OpenSelectedHelpCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOtherSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewSectionSelected));
        OnPropertyChanged(nameof(IsHelpSectionSelected));
        OnPropertyChanged(nameof(IsToolBoxSectionSelected));
        OnPropertyChanged(nameof(IsFeedbackSectionSelected));
        OnPropertyChanged(nameof(IsFeatureHubSectionSelected));
        OnPropertyChanged(nameof(IsAboutSectionSelected));
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (_updateCheck is null)
        {
            UpdateStatusText = "更新检查服务未初始化";
            return;
        }

        try
        {
            UpdateStatusText = "正在检查更新...";
            var info = await _updateCheck.CheckAsync();
            UpdateStatusText = info.IsUpdateAvailable
                ? $"发现新版本：{info.LatestVersion}，当前版本：{info.CurrentVersion}"
                : $"当前已是最新版本：{info.CurrentVersion}";
            FeatureHubStatusText = "更新检查完成";
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "检查更新失败");
            UpdateStatusText = "检查更新失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void RefreshFeatureHub()
    {
        if (_featureHub is null)
        {
            FeatureModules =
            [
                new("更新系统", "占位", "等待服务容器接入。", "接入 AppUpdateCheckService。"),
                new("崩溃分析", "占位", "等待服务容器接入。", "接入 CrashAnalysisService。"),
                new("账号管理中心", "占位", "等待服务容器接入。", "接入账号缓存与列表。")
            ];
            HomeFeedItems = [];
            ExtensionPoints = [];
            FeatureHubStatusText = "功能枢纽服务未初始化";
            return;
        }

        FeatureModules = _featureHub.GetModules();
        HomeFeedItems = _featureHub.GetHomeFeedItems();
        ExtensionPoints = _featureHub.GetExtensionPoints();

        var crash = _featureHub.AnalyzeCrashes();
        CrashAnalysisText = string.IsNullOrWhiteSpace(crash.LatestReportPath)
            ? crash.Status
            : $"{crash.Status}\n最近报告：{crash.LatestReportPath}\n报告数量：{crash.ReportCount}";

        var account = _featureHub.GetAccountSummary();
        AccountCenterText = $"{account.Status}\n当前登录：{account.CurrentLoginType}\n显示名称：{account.CurrentDisplayName}\n缓存账号：{account.CachedAccountCount}";

        var skin = _featureHub.GetSkinSummary();
        SkinCenterText = $"{skin.Status}\n皮肤模式：{skin.SkinMode}\n皮肤标识：{skin.SkinIdentity}\nSlim：{(skin.SlimModel ? "是" : "否")}";

        ExtensionPointText = $"已登记 {ExtensionPoints.Count} 个扩展点；当前先做目录与权限边界占位。";
        FeatureHubStatusText = "实验与规划信息已刷新";
    }

    partial void OnCustomDownloadUrlChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(CustomDownloadFileName))
        {
            CustomDownloadFileName = InferFileNameFromUrl(value);
        }

        StartCustomDownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnCustomDownloadFolderChanged(string value)
    {
        StartCustomDownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnCustomDownloadFileNameChanged(string value)
    {
        StartCustomDownloadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RollLuck()
    {
        var seedText = DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
            + "|"
            + Environment.UserName;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seedText));
        var value = BitConverter.ToUInt32(hash, 0) % 101;
        var comment = value switch
        {
            >= 90 => "离谱地好，可以趁热修 Bug。",
            >= 70 => "不错，今天适合启动整合包。",
            <= 10 => "谨慎点，先备份再动手。",
            <= 30 => "一般般，喝口水再继续。",
            _ => "平稳发挥，继续推进。"
        };
        ToolboxStatusText = $"今日人品：{value}，{comment}";
    }

    [RelayCommand]
    private async Task OptimizeMemoryAsync()
    {
        if (_memoryOptimizer is null)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            ToolboxStatusText = "已执行基础内存优化";
            return;
        }

        try
        {
            ToolboxStatusText = "正在执行内存优化";
            var result = await _memoryOptimizer.OptimizeAsync();
            ToolboxStatusText = "内存优化完成，已处理进程数：" + result.ProcessCount;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "百宝箱内存优化失败");
            ToolboxStatusText = "内存优化失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void CleanGameTrash()
    {
        var root = GetMinecraftRootPath();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            ToolboxStatusText = "未找到 Minecraft 文件夹，请先在启动或下载页选择根目录。";
            return;
        }

        if (ShouldConfirmDangerousActions()
            && _prompts?.Confirm("清理游戏垃圾", "将清理临时 natives、旧压缩日志和临时残留，不会删除存档或版本主体。是否继续？") == false)
        {
            ToolboxStatusText = "已取消清理游戏垃圾";
            return;
        }

        try
        {
            var result = CleanGameTrash(root);
            ToolboxStatusText = result.DeletedCount == 0
                ? "没有发现可安全清理的游戏垃圾。"
                : $"已清理 {result.DeletedCount} 项，释放 {FormatBytes(result.Bytes)}。";
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "清理游戏垃圾失败");
            ToolboxStatusText = "清理游戏垃圾失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void DoNotClick()
    {
        _doNotClickCount++;
        ToolboxStatusText = _doNotClickCount switch
        {
            1 => "都说了千万别点。好吧，已经点了一次。",
            2 => "第二次了，PCL Sharp 正在假装严肃地记录这件事。",
            3 => "第三次。放心，这个按钮不会删除世界，只会增加一点戏剧性。",
            _ => $"你已经点了 {_doNotClickCount} 次。按钮本人表示情绪稳定。"
        };
    }

    [RelayCommand]
    private void OpenEchoCave()
    {
        var messages = new[]
        {
            "回声洞：截图里的红框基本都是下一轮 UI 优先级。",
            "回声洞：PCL Sharp 仍是实验品，但会持续向原版行为靠齐。",
            "回声洞：不要用滚动条偷懒，这条已经刻在待办墙上了。",
            "回声洞：如果启动链出问题，日志和可复现路径永远第一优先。"
        };
        var index = Math.Abs(HashCode.Combine(DateTime.Now.Second, _doNotClickCount)) % messages.Length;
        EchoCaveText = messages[index];
        ToolboxStatusText = "已刷新回声洞留言";
    }

    [RelayCommand]
    private void BrowseCustomDownloadFolder()
    {
        if (_fileDialogs is null)
        {
            CustomDownloadStatusText = "文件夹选择器未初始化";
            return;
        }

        var selected = _fileDialogs.PickFolder("选择自定义下载保存位置", CustomDownloadFolder);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            CustomDownloadFolder = selected;
            CustomDownloadStatusText = "保存位置已更新：" + selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCustomDownload))]
    private async Task StartCustomDownloadAsync()
    {
        if (_downloadManager is null)
        {
            CustomDownloadStatusText = "下载管理器未初始化";
            return;
        }

        var url = CustomDownloadUrl.Trim();
        var fileName = SanitizeFileName(string.IsNullOrWhiteSpace(CustomDownloadFileName) ? InferFileNameFromUrl(url) : CustomDownloadFileName);
        var folder = CustomDownloadFolder.Trim();
        var localPath = Path.Combine(folder, fileName);
        if (ShouldConfirmDangerousActions()
            && _prompts?.Confirm("下载自定义文件", $"将从以下地址下载文件：\n{url}\n\n保存到：\n{localPath}\n\n是否继续？") == false)
        {
            CustomDownloadStatusText = "已取消自定义文件下载";
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            CustomDownloadFileName = fileName;
            CustomDownloadStatusText = "已创建下载任务：" + fileName;
            var file = new DownloadFile([url], localPath, new DownloadFileCheck(MinSize: 1, CanUseExistingFile: false), SimulateBrowserHeaders: true);
            var snapshot = await _downloadManager.DownloadAsync("自定义下载：" + fileName, [file]);
            CustomDownloadStatusText = snapshot.State switch
            {
                DownloadTaskState.Succeeded => "自定义文件下载完成：" + localPath,
                DownloadTaskState.Canceled => "自定义文件下载已取消：" + fileName,
                DownloadTaskState.Failed => "自定义文件下载失败：" + snapshot.Message,
                _ => snapshot.Message
            };
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "自定义文件下载失败");
            CustomDownloadStatusText = "自定义文件下载失败：" + ex.Message;
        }
    }

    private bool CanStartCustomDownload()
    {
        return Uri.TryCreate(CustomDownloadUrl.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(CustomDownloadFolder)
            && !string.IsNullOrWhiteSpace(CustomDownloadFileName);
    }

    private bool ShouldConfirmDangerousActions()
    {
        return _settings?.Get(AppSettingKeys.AccessibilityConfirmDangerousActions, true) ?? true;
    }

    private string GetMinecraftRootPath()
    {
        var saved = _settings?.Get(AppSettingKeys.MinecraftRootPath, "") ?? "";
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");
    }

    private static string InferFileNameFromUrl(string url)
    {
        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(Uri.UnescapeDataString(uri.LocalPath));
            if (!string.IsNullOrWhiteSpace(name))
            {
                return SanitizeFileName(name);
            }
        }

        return "download.file";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "download.file" : cleaned;
    }

    private static (int DeletedCount, long Bytes) CleanGameTrash(string root)
    {
        var candidates = new List<FileSystemInfo>();
        var rootInfo = new DirectoryInfo(root);
        AddFiles(candidates, Path.Combine(root, "logs"), "*.log.gz");
        AddFiles(candidates, Path.Combine(root, "logs"), "*.log.tmp");
        AddFiles(candidates, Path.Combine(root, "logs"), "*.tmp");
        AddFiles(candidates, Path.Combine(root, "crash-reports"), "*.tmp");

        var versions = Path.Combine(root, "versions");
        if (Directory.Exists(versions))
        {
            foreach (var directory in Directory.EnumerateDirectories(versions, "*-natives", SearchOption.AllDirectories))
            {
                candidates.Add(new DirectoryInfo(directory));
            }
        }

        var deleted = 0;
        long bytes = 0;
        foreach (var item in candidates)
        {
            try
            {
                if (!IsUnderRoot(rootInfo.FullName, item.FullName))
                {
                    continue;
                }

                bytes += item is FileInfo file ? file.Length : GetDirectorySize((DirectoryInfo)item);
                if (item is DirectoryInfo directory)
                {
                    directory.Delete(recursive: true);
                }
                else
                {
                    item.Delete();
                }

                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
            {
            }
        }

        return (deleted, bytes);
    }

    private static void AddFiles(List<FileSystemInfo> candidates, string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        candidates.AddRange(Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)));
    }

    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        return directory.Exists
            ? directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
            : 0;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    [RelayCommand]
    private async Task RefreshHelpAsync()
    {
        _allHelpEntries = [];
        HelpResults = [];
        SelectedHelpEntry = null;
        await OnNavigatedToAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedHelp))]
    private async Task OpenSelectedHelpAsync()
    {
        if (SelectedHelpEntry is null || _helpActions is null)
        {
            return;
        }

        try
        {
            if (SelectedHelpEntry.IsEvent && await TryExecutePageHelpActionAsync(SelectedHelpEntry))
            {
                return;
            }

            var result = await _helpActions.ExecuteAsync(SelectedHelpEntry);
            HelpActionStatusText = result.Message;
            if (!result.Success)
            {
                _logger?.Warn(result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "执行帮助条目动作失败");
            HelpActionStatusText = "执行帮助条目动作失败：" + ex.Message;
        }
    }

    private bool CanOpenSelectedHelp()
    {
        return SelectedHelpEntry is not null && _helpActions is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteHelpDocumentBlock))]
    private async Task ExecuteHelpDocumentBlockAsync(HelpDocumentBlock? block)
    {
        if (block is null || !block.HasAction)
        {
            return;
        }

        try
        {
            if (block.Events.Count > 0)
            {
                await ExecuteHelpDocumentEventsAsync(block);
                return;
            }

            if (await TryExecutePageHelpActionAsync(block))
            {
                return;
            }

            await ExecuteExternalHelpActionAsync(block.Title, block.Text, block.EventType, block.EventData);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "执行帮助正文事件失败");
            HelpActionStatusText = "执行帮助正文事件失败：" + ex.Message;
        }
    }

    private bool CanExecuteHelpDocumentBlock(HelpDocumentBlock? block)
    {
        return block?.HasAction == true;
    }

    private async Task ExecuteHelpDocumentEventsAsync(HelpDocumentBlock block)
    {
        var executed = 0;
        foreach (var item in block.Events)
        {
            var eventBlock = new HelpDocumentBlock("Event", block.Title, block.Text, item.EventType, item.EventData);
            if (!await TryExecutePageHelpActionAsync(eventBlock))
            {
                await ExecuteExternalHelpActionAsync(block.Title, block.Text, item.EventType, item.EventData);
            }

            executed++;
        }

        HelpActionStatusText = $"已执行 {executed} 个页面事件";
    }

    private async Task ExecuteExternalHelpActionAsync(string title, string text, string eventType, string eventData)
    {
        if (_helpActions is null)
        {
            HelpActionStatusText = "帮助动作服务未初始化";
            return;
        }

        var result = await _helpActions.ExecuteAsync(new HelpEntry(
            string.IsNullOrWhiteSpace(title) ? SelectedHelpEntry?.Title ?? "帮助事件" : title,
            text,
            "",
            SelectedHelpEntry?.Types ?? [],
            SelectedHelpEntry?.RawPath ?? "",
            true,
            eventType,
            eventData,
            "",
            true,
            true,
            true));
        HelpActionStatusText = result.Message;
        if (!result.Success)
        {
            _logger?.Warn(result.Message);
        }
    }

    private Task<bool> TryExecutePageHelpActionAsync(HelpEntry entry)
    {
        return TryExecutePageHelpActionAsync(new HelpDocumentBlock(
            "Event",
            entry.Title,
            entry.Description,
            entry.EventType,
            entry.EventData));
    }

    private async Task<bool> TryExecutePageHelpActionAsync(HelpDocumentBlock block)
    {
        if (block.EventType.Equals(HelpActionService.EventRefreshHelp, StringComparison.OrdinalIgnoreCase)
            || block.EventType.Equals(HelpActionService.EventRefreshPage, StringComparison.OrdinalIgnoreCase))
        {
            await RefreshHelpAsync();
            HelpActionStatusText = block.EventType.Equals(HelpActionService.EventRefreshPage, StringComparison.OrdinalIgnoreCase)
                ? "已刷新当前页面"
                : "已刷新帮助列表";
            return true;
        }

        if (block.EventType.Equals(HelpActionService.EventOpenHelp, StringComparison.OrdinalIgnoreCase))
        {
            var entry = FindHelpEntry(block.EventData);
            if (entry is null)
            {
                HelpActionStatusText = string.IsNullOrWhiteSpace(block.EventData)
                    ? "未指定要打开的帮助条目"
                    : "未找到帮助条目：" + block.EventData;
                return true;
            }

            if (!HelpResults.Contains(entry))
            {
                HelpSearchText = "";
                ApplyHelpSearch();
            }

            SelectedHelpEntry = entry;
            HelpActionStatusText = "已打开帮助：" + entry.Title;
            return true;
        }

        return false;
    }

    private HelpEntry? FindHelpEntry(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalized = query.Trim();
        return _allHelpEntries.FirstOrDefault(entry =>
            entry.Title.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || entry.RawPath.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? _allHelpEntries.FirstOrDefault(entry =>
                entry.RawPath.EndsWith(normalized, StringComparison.OrdinalIgnoreCase)
                || entry.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task OpenAboutLinkAsync(AboutLink? link)
    {
        if (link is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(link.Url))
        {
            AboutActionStatusText = link.Title + "：暂无可打开链接";
            return;
        }

        if (_helpActions is null)
        {
            AboutActionStatusText = "链接服务未初始化";
            return;
        }

        try
        {
            var result = await _helpActions.ExecuteAsync(new HelpEntry(
                link.Title,
                link.Description,
                "",
                [link.Group],
                link.Url,
                true,
                HelpActionService.EventOpenWebsite,
                link.Url,
                "",
                true,
                true,
                true));
            AboutActionStatusText = result.Message;
            if (!result.Success)
            {
                _logger?.Warn(result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "打开关于链接失败");
            AboutActionStatusText = "打开关于链接失败：" + ex.Message;
        }
    }

    private void ApplyHelpSearch()
    {
        if (_help is null)
        {
            HelpStatusText = "帮助服务未初始化";
            return;
        }

        HelpResults = _help.Search(_allHelpEntries, HelpSearchText, 80);
        HelpStatusText = string.IsNullOrWhiteSpace(HelpSearchText)
            ? $"已加载 {_allHelpEntries.Count} 个帮助条目"
            : $"找到 {HelpResults.Count} 个帮助条目";
        if (SelectedHelpEntry is null || !HelpResults.Contains(SelectedHelpEntry))
        {
            SelectedHelpEntry = HelpResults.FirstOrDefault();
        }
    }
}
