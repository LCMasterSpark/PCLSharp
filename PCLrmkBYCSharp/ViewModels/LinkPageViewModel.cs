using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Link;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LinkPageViewModel : PageViewModelBase
{
    private readonly ILinkService _linkService;
    private readonly IAppSettingsService _settings;
    private readonly IAppLoggerService _logger;
    private readonly IExternalUrlService? _urls;

    public LinkPageViewModel(
        ILinkService linkService,
        IAppSettingsService settings,
        IAppLoggerService logger,
        IExternalUrlService? urls = null)
        : base(PageRoute.Link, "陶瓦联机", "Terracotta / EasyTier 联机入口")
    {
        _linkService = linkService;
        _settings = settings;
        _logger = logger;
        _urls = urls;

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
    private string generatedInviteCode = "";

    [ObservableProperty]
    private string shareText = "生成房间后会在这里显示可复制的邀请码。";

    [ObservableProperty]
    private string parsedInviteSummary = "输入 PCL 邀请码后可先验证格式；真正启动联机进程会在后续阶段接入。";

    [ObservableProperty]
    private string statusMessage = "旧 PCL 的联机逻辑已找到，但旧入口曾被隐藏；当前先保留陶瓦联机 / EasyTier 的接入位置。";

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
            StatusMessage = "已生成房间邀请码。联机进程启动、节点发现和端口转发会在下一阶段接入。";
            _logger.Info("已生成陶瓦联机占位邀请码。");
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
        StatusMessage = "邀请码可识别，后续会按陶瓦联机 / EasyTier 的实际启动器接入加入房间。";
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

        StatusMessage = "已准备加入房间。当前版本仅占位，不会启动 Terracotta 或 EasyTier 进程。";
    }

    [RelayCommand]
    private Task SaveLinkSettingsAsync()
    {
        _settings.Set(AppSettingKeys.LinkProvider, SelectedProvider.Value);
        _settings.Set(AppSettingKeys.LinkLatencyMode, SelectedLatencyMode.Value);
        _settings.Set(AppSettingKeys.LinkCustomPeer, CustomPeer);
        _settings.Set(AppSettingKeys.LinkLastInviteCode, InviteCodeInput);
        _settings.Set(AppSettingKeys.LinkServerPort, ServerPort);
        StatusMessage = string.IsNullOrWhiteSpace(StatusMessage) ? "联机设置已保存。" : StatusMessage;
        return _settings.SaveAsync();
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
}

public sealed record LinkProviderOption(LinkProviderKind Value, string DisplayName, string Description);

public sealed record LinkLatencyOption(LinkLatencyMode Value, string DisplayName, string Description);
