using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

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
    private int selectedOtherSection;

    public OtherPageViewModel(IAppPathService? paths = null, IHelpService? help = null, IAppLoggerService? logger = null, IHelpActionService? helpActions = null)
        : base(PageRoute.Other, "更多", "帮助、关于、诊断、反馈与维护工具")
    {
        _help = help;
        _logger = logger;
        _helpActions = helpActions;
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
            new("关于", "龙腾猫跃", "Plain Craft Launcher 的开发者", null),
            new("关于", "Plain Craft Launcher Sharp", $"简称：PCL Sharp / PCL#\n程序集版本：{version}", null),
            new("入口", "赞助 PCL", "打开爱发电赞助页面", "https://meloong.com/afd/a/LTCat"),
            new("入口", "查看源代码", "打开 PCL2 GitHub 仓库", "https://github.com/Hex-Dragon/PCL2"),
            new("特别鸣谢", "bangbang93", "BMCLAPI 镜像源和 Forge 安装工具", "https://bmclapi.bangbang93.com"),
            new("特别鸣谢", "MC 百科", "Mod 中文名称与相关信息", "https://www.mcmod.cn"),
            new("特别鸣谢", "MCIM", "社区资源镜像源和帮助库图库", "https://github.com/mcmod-info-mirror"),
            new("特别鸣谢", "EasyTier", "联机模块支持", "https://github.com/EasyTier/EasyTier"),
            new("特别鸣谢", "00ll00", "Java Launch Wrapper 和相关服务支持", null),
            new("特别鸣谢", "Patrick", "PCL 图标设计", null),
            new("特别鸣谢", "Hao_Tian", "内测与异常问题反馈", null)
        ];
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

    public IReadOnlyList<AboutLink> AboutLinks { get; }

    public IReadOnlyList<OtherSectionOption> OtherSections { get; } =
    [
        new(0, "概览", "帮助、诊断、关于与社区"),
        new(1, "帮助系统", "搜索并打开内置帮助条目")
    ];

    public bool IsOverviewSectionSelected => SelectedOtherSection == 0;

    public bool IsHelpSectionSelected => SelectedOtherSection == 1;

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
