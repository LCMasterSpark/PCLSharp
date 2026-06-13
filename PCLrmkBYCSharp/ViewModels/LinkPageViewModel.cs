using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Link;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LinkPageViewModel : PageViewModelBase
{
    private readonly ILinkService _linkService;
    private readonly ILinkBackendService _linkBackend;
    private readonly IAppSettingsService _settings;
    private readonly IAppLoggerService _logger;
    private readonly IExternalUrlService? _urls;
    private readonly IFileDialogService? _fileDialogs;

    public LinkPageViewModel(
        ILinkService linkService,
        IAppSettingsService settings,
        IAppLoggerService logger,
        IExternalUrlService? urls = null,
        ILinkBackendService? linkBackend = null,
        IFileDialogService? fileDialogs = null)
        : base(PageRoute.Link, "陶瓦联机", "Terracotta / EasyTier 联机入口")
    {
        _linkService = linkService;
        _linkBackend = linkBackend ?? new LinkBackendService();
        _settings = settings;
        _logger = logger;
        _urls = urls;
        _fileDialogs = fileDialogs;

        ProviderOptions =
        [
            new LinkProviderOption(LinkProviderKind.Terracotta, "陶瓦联机", "面向 Minecraft 的联机方案，基于 EasyTier，后续优先接入。"),
            new LinkProviderOption(LinkProviderKind.EasyTier, "EasyTier", "保留底层 VPN 模式，适合手动节点和高级配置。")
        ];
        LatencyOptions =
        [
            new LinkLatencyOption(LinkLatencyMode.DirectFirst, "优先直连", "更贴近旧 PCL 的默认体验，优先尝试直连。"),
            new LinkLatencyOption(LinkLatencyMode.LatencyFirst, "优先低延迟", "优先选择延迟更低的中继或路径。")
        ];

        selectedProvider = ProviderOptions.First(item => item.Value == _settings.Get(AppSettingKeys.LinkProvider, LinkProviderKind.Terracotta));
        selectedLatencyMode = LatencyOptions.First(item => item.Value == _settings.Get(AppSettingKeys.LinkLatencyMode, LinkLatencyMode.DirectFirst));
        customPeer = _settings.Get(AppSettingKeys.LinkCustomPeer, "");
        inviteCodeInput = _settings.Get(AppSettingKeys.LinkLastInviteCode, "");
        serverPort = _settings.Get(AppSettingKeys.LinkServerPort, 25565);
        terracottaExecutablePath = _settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, "");
        easyTierExecutablePath = _settings.Get(AppSettingKeys.LinkEasyTierExecutablePath, "");
        RefreshBackendStatus();
    }

    public IReadOnlyList<LinkProviderOption> ProviderOptions { get; }

    public IReadOnlyList<LinkLatencyOption> LatencyOptions { get; }

    [ObservableProperty]
    private LinkProviderOption selectedProvider;

    [ObservableProperty]
    private LinkLatencyOption selectedLatencyMode;

    [ObservableProperty]
    private string customPeer = "";

    [ObservableProperty]
    private string inviteCodeInput = "";

    [ObservableProperty]
    private int serverPort = 25565;

    [ObservableProperty]
    private string terracottaExecutablePath = "";

    [ObservableProperty]
    private string easyTierExecutablePath = "";

    [ObservableProperty]
    private string generatedInviteCode = "";

    [ObservableProperty]
    private string shareText = "生成房间后会在这里显示可复制的邀请码。";

    [ObservableProperty]
    private string parsedInviteSummary = "输入 PCL 邀请码后可先验证格式；真正启动联机进程会在后续阶段接入。";

    [ObservableProperty]
    private string statusMessage = "已保留陶瓦联机 / EasyTier 的接入位置；当前会检测后端二进制并生成启动计划。";

    [ObservableProperty]
    private string backendStatusText = "";

    [ObservableProperty]
    private string backendPlanText = "生成或验证房间后会在这里显示联机启动计划。";

    public bool HasUrlService => _urls is not null;

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        try
        {
            var invite = _linkService.CreateHostInvite(ServerPort);
            GeneratedInviteCode = _linkService.BuildInviteCode(invite);
            InviteCodeInput = GeneratedInviteCode;
            ShareText = _linkService.BuildShareText(invite);
            ParsedInviteSummary = $"房间端口：{invite.ServerPort}，网络：{invite.NetworkName}，协议版本：{invite.Version}";
            UpdateBackendPlan(LinkRoomRole.Host, invite);
            StatusMessage = "已生成房间邀请码和联机启动计划。";
            _logger.Info("已生成陶瓦联机邀请码和启动计划。");
            await SaveLinkSettingsAsync();
        }
        catch (ArgumentOutOfRangeException)
        {
            StatusMessage = "端口号必须在 1024 到 65535 之间。";
        }
    }

    [RelayCommand]
    private async Task ValidateInviteAsync()
    {
        var result = _linkService.ParseInviteCode(InviteCodeInput);
        if (!result.Success || result.Invite is null)
        {
            ParsedInviteSummary = result.Message;
            StatusMessage = "邀请码校验失败：" + result.Message;
            return;
        }

        ParsedInviteSummary = $"邀请码有效。端口：{result.Invite.ServerPort}，网络：{result.Invite.NetworkName}，协议版本：{result.Invite.Version}";
        UpdateBackendPlan(LinkRoomRole.Joiner, result.Invite);
        StatusMessage = "邀请码可识别，已生成加入房间的联机启动计划。";
        await SaveLinkSettingsAsync();
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        await ValidateInviteAsync();
        if (!ParsedInviteSummary.StartsWith("邀请码有效。", StringComparison.Ordinal))
        {
            return;
        }

        StatusMessage = BackendPlanText.Contains("后端已就绪", StringComparison.Ordinal)
            ? "联机启动计划已生成，下一步会接入真实进程启动。"
            : "已生成联机启动计划，但后端二进制尚未配置，暂不会启动进程。";
    }

    [RelayCommand]
    private Task SaveLinkSettingsAsync()
    {
        _settings.Set(AppSettingKeys.LinkProvider, SelectedProvider.Value);
        _settings.Set(AppSettingKeys.LinkLatencyMode, SelectedLatencyMode.Value);
        _settings.Set(AppSettingKeys.LinkCustomPeer, CustomPeer);
        _settings.Set(AppSettingKeys.LinkLastInviteCode, InviteCodeInput);
        _settings.Set(AppSettingKeys.LinkServerPort, ServerPort);
        _settings.Set(AppSettingKeys.LinkTerracottaExecutablePath, TerracottaExecutablePath);
        _settings.Set(AppSettingKeys.LinkEasyTierExecutablePath, EasyTierExecutablePath);
        RefreshBackendStatus();
        StatusMessage = string.IsNullOrWhiteSpace(StatusMessage) ? "联机设置已保存。" : StatusMessage;
        return _settings.SaveAsync();
    }

    [RelayCommand]
    private void RefreshBackendStatus()
    {
        var status = _linkBackend.GetStatus(SelectedProvider.Value, GetSelectedExecutablePath());
        BackendStatusText = status.CanStart
            ? status.Message + " 路径：" + status.ExecutablePath
            : status.Message;
    }

    [RelayCommand]
    private async Task PickSelectedBackendExecutableAsync()
    {
        if (_fileDialogs is null)
        {
            StatusMessage = "当前环境没有可用的文件选择器。";
            return;
        }

        var provider = SelectedProvider.Value;
        var title = provider == LinkProviderKind.Terracotta ? "选择 Terracotta 可执行文件" : "选择 EasyTier 可执行文件";
        var selected = _fileDialogs.PickExecutable(title, GetExecutableInitialDirectory(), "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(selected))
        {
            StatusMessage = "已取消选择联机后端。";
            return;
        }

        if (provider == LinkProviderKind.Terracotta)
        {
            TerracottaExecutablePath = selected;
        }
        else
        {
            EasyTierExecutablePath = selected;
        }

        StatusMessage = "联机后端路径已更新。";
        await SaveLinkSettingsAsync();
    }

    [RelayCommand]
    private void OpenTerracotta()
    {
        _urls?.OpenUrl("https://github.com/burningtnt/Terracotta");
    }

    [RelayCommand]
    private void OpenEasyTier()
    {
        _urls?.OpenUrl("https://github.com/EasyTier/EasyTier");
    }

    partial void OnSelectedProviderChanged(LinkProviderOption value)
    {
        RefreshBackendStatus();
    }

    partial void OnTerracottaExecutablePathChanged(string value)
    {
        if (SelectedProvider.Value == LinkProviderKind.Terracotta)
        {
            RefreshBackendStatus();
        }
    }

    partial void OnEasyTierExecutablePathChanged(string value)
    {
        if (SelectedProvider.Value == LinkProviderKind.EasyTier)
        {
            RefreshBackendStatus();
        }
    }

    private void UpdateBackendPlan(LinkRoomRole role, LinkInviteInfo invite)
    {
        var plan = _linkBackend.CreatePlan(role, SelectedProvider.Value, invite, SelectedLatencyMode.Value, CustomPeer, GetSelectedExecutablePath());
        var options = string.Join(Environment.NewLine, plan.PlannedOptions.Select(option => "  " + option));
        BackendPlanText = plan.CanStart
            ? plan.Summary + Environment.NewLine + "后端已就绪，计划参数：" + Environment.NewLine + options
            : plan.Summary + Environment.NewLine + "暂不能启动：" + plan.BlockReason + Environment.NewLine + "计划参数：" + Environment.NewLine + options;
    }

    private string GetSelectedExecutablePath()
    {
        return SelectedProvider.Value == LinkProviderKind.Terracotta
            ? TerracottaExecutablePath
            : EasyTierExecutablePath;
    }

    private string GetExecutableInitialDirectory()
    {
        var path = GetSelectedExecutablePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }

        var directory = Path.GetDirectoryName(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : directory;
    }
}

public sealed record LinkProviderOption(LinkProviderKind Value, string DisplayName, string Description);

public sealed record LinkLatencyOption(LinkLatencyMode Value, string DisplayName, string Description);
