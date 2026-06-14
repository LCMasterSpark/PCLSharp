using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ILinkProcessService? _linkProcess;
    private readonly IUiDispatcherService? _dispatcher;
    private readonly IClipboardService? _clipboard;
    private readonly IFolderOpenService? _folders;
    private readonly IAppPathService? _paths;
    private LinkBackendLaunchPlan? _currentPlan;

    public LinkPageViewModel(
        ILinkService linkService,
        IAppSettingsService settings,
        IAppLoggerService logger,
        IExternalUrlService? urls = null,
        ILinkBackendService? linkBackend = null,
        IFileDialogService? fileDialogs = null,
        ILinkProcessService? linkProcess = null,
        IUiDispatcherService? dispatcher = null,
        IClipboardService? clipboard = null,
        IFolderOpenService? folders = null,
        IAppPathService? paths = null)
        : base(PageRoute.Link, "陶瓦联机", "Terracotta / EasyTier 联机入口")
    {
        _linkService = linkService;
        _linkBackend = linkBackend ?? new LinkBackendService();
        _settings = settings;
        _logger = logger;
        _urls = urls;
        _fileDialogs = fileDialogs;
        _linkProcess = linkProcess;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _folders = folders;
        _paths = paths;
        if (_linkProcess is not null)
        {
            _linkProcess.SnapshotChanged += HandleProcessSnapshotChanged;
        }

        ProviderOptions =
        [
            new LinkProviderOption(LinkProviderKind.Terracotta, "陶瓦联机", "面向 Minecraft 的联机方案，基于 EasyTier，后续优先接入。"),
            new LinkProviderOption(LinkProviderKind.EasyTier, "EasyTier", "保留底层 VPN 模式，适合手动节点和高级配置。")
        ];
        LatencyOptions =
        [
            new LinkLatencyOption(LinkLatencyMode.DirectFirst, "优先直连", "更贴近原 PCL 的默认体验，优先尝试直连。"),
            new LinkLatencyOption(LinkLatencyMode.LatencyFirst, "优先低延迟", "优先选择延迟更低的中继或路径。")
        ];

        selectedProvider = GetProviderOption(_settings.Get(AppSettingKeys.LinkProvider, LinkProviderKind.Terracotta));
        selectedLatencyMode = GetLatencyOption(_settings.Get(AppSettingKeys.LinkLatencyMode, LinkLatencyMode.DirectFirst));
        LoadLinkSettingsFromStore();
        RefreshBackendStatus();
        RestoreSavedInviteCode();
        RefreshProcessSnapshotFromService();
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
    private string statusMessage = "已保留陶瓦联机 / EasyTier 的接入位置；当前会检测后端二进制、生成启动计划并采集后端日志。";

    [ObservableProperty]
    private string backendStatusText = "";

    [ObservableProperty]
    private string backendPlanText = "生成或验证房间后会在这里显示联机启动计划。";

    [ObservableProperty]
    private string linkProcessStatusText = "联机后端未启动。";

    [ObservableProperty]
    private string linkConnectionStatusText = "联机后端未运行。";

    [ObservableProperty]
    private string linkConnectedPeersText = "暂无已连接节点。";

    [ObservableProperty]
    private string linkProcessLogText = "联机后端输出会显示在这里。";

    public bool HasUrlService => _urls is not null;

    public override Task OnNavigatedToAsync()
    {
        LoadLinkSettingsFromStore();
        RefreshBackendStatus();
        RestoreSavedInviteCode();
        RefreshProcessSnapshotFromService();
        return Task.CompletedTask;
    }

    public async Task LoadInviteCodeAsync(string inviteCode)
    {
        InviteCodeInput = inviteCode?.Trim() ?? "";
        await JoinRoomAsync();
    }

    [RelayCommand]
    private void CopyInviteCode()
    {
        var text = string.IsNullOrWhiteSpace(GeneratedInviteCode)
            ? InviteCodeInput
            : GeneratedInviteCode;
        CopyText(text, "邀请码");
    }

    [RelayCommand]
    private void CopyShareText()
    {
        CopyText(ShareText, "分享文本");
    }

    [RelayCommand]
    private void CopyLaunchPlan()
    {
        CopyText(BackendPlanText, "启动计划");
    }

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
        if (!TryBuildJoinPlanFromInvite(out var failureMessage))
        {
            ParsedInviteSummary = failureMessage;
            StatusMessage = "邀请码校验失败：" + failureMessage;
            return;
        }

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
            ? "联机启动计划已生成，可以尝试启动联机后端。"
            : "已生成联机启动计划，但后端二进制尚未配置，暂不会启动进程。";
    }

    [RelayCommand]
    private void StartBackend()
    {
        if (_linkProcess is null)
        {
            ApplyProcessStatus("当前环境没有联机进程服务。");
            return;
        }

        if (_currentPlan is null)
        {
            ApplyProcessStatus("请先创建房间或验证邀请码，生成联机启动计划。");
            return;
        }

        ApplyProcessSnapshot(_linkProcess.Start(_currentPlan));
    }

    [RelayCommand]
    private void StopBackend()
    {
        if (_linkProcess is null)
        {
            ApplyProcessStatus("当前环境没有联机进程服务。");
            return;
        }

        ApplyProcessSnapshot(_linkProcess.Stop());
    }

    [RelayCommand]
    private void RetryBackend()
    {
        if (_currentPlan is null)
        {
            ApplyProcessStatus("请先创建房间或验证邀请码，生成联机启动计划。");
            return;
        }

        if (_linkProcess is not null && _linkProcess.Current.State == LinkProcessState.Running)
        {
            ApplyProcessStatus("联机后端仍在运行，无需重试。");
            RefreshProcessSnapshotFromService();
            return;
        }

        StartBackend();
    }

    [RelayCommand]
    private void OpenLinkLogs()
    {
        if (_folders is null || _paths is null)
        {
            ApplyProcessStatus("当前环境没有可用的日志目录打开服务。");
            return;
        }

        try
        {
            _paths.EnsureCreated();
            _folders.OpenFolder(_paths.LogsDirectory);
            StatusMessage = "已打开 PCL Sharp 日志目录。";
        }
        catch (Exception ex)
        {
            StatusMessage = "打开日志目录失败：" + ex.Message;
            _logger.Error(ex, "打开联机日志目录失败");
        }
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
    private async Task AutoDetectBackendExecutableAsync()
    {
        var selected = _linkBackend.FindExecutable(SelectedProvider.Value, GetBackendSearchRoots());
        if (string.IsNullOrWhiteSpace(selected))
        {
            StatusMessage = SelectedProvider.DisplayName + " 后端未在常见目录中找到。";
            RefreshBackendStatus();
            return;
        }

        if (SelectedProvider.Value == LinkProviderKind.Terracotta)
        {
            TerracottaExecutablePath = selected;
        }
        else
        {
            EasyTierExecutablePath = selected;
        }

        StatusMessage = "已自动找到联机后端：" + selected;
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
        _currentPlan = plan;
        var options = string.Join(Environment.NewLine, plan.PlannedOptions.Select(option => "  " + option));
        BackendPlanText = plan.CanStart
            ? plan.Summary + Environment.NewLine + "后端已就绪，计划参数：" + Environment.NewLine + options
            : plan.Summary + Environment.NewLine + "暂不能启动：" + plan.BlockReason + Environment.NewLine + "计划参数：" + Environment.NewLine + options;
    }

    private void RestoreSavedInviteCode()
    {
        if (string.IsNullOrWhiteSpace(InviteCodeInput))
        {
            return;
        }

        if (TryBuildJoinPlanFromInvite(out var failureMessage))
        {
            StatusMessage = "已恢复上次联机邀请码，联机启动计划已生成。";
            return;
        }

        ParsedInviteSummary = failureMessage;
        StatusMessage = "上次联机邀请码无法识别：" + failureMessage;
    }

    private void LoadLinkSettingsFromStore()
    {
        SelectedProvider = GetProviderOption(_settings.Get(AppSettingKeys.LinkProvider, LinkProviderKind.Terracotta));
        SelectedLatencyMode = GetLatencyOption(_settings.Get(AppSettingKeys.LinkLatencyMode, LinkLatencyMode.DirectFirst));
        CustomPeer = _settings.Get(AppSettingKeys.LinkCustomPeer, "");
        InviteCodeInput = _settings.Get(AppSettingKeys.LinkLastInviteCode, "");
        ServerPort = _settings.Get(AppSettingKeys.LinkServerPort, 25565);
        TerracottaExecutablePath = _settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, "");
        EasyTierExecutablePath = _settings.Get(AppSettingKeys.LinkEasyTierExecutablePath, "");
    }

    private void RefreshProcessSnapshotFromService()
    {
        if (_linkProcess is not null)
        {
            var snapshot = _linkProcess.Current;
            var shouldUpdatePageStatus = snapshot.State != LinkProcessState.Stopped
                || snapshot.RecentLogLines.Count > 0
                || !string.IsNullOrWhiteSpace(snapshot.CommandPreview);
            ApplyProcessSnapshot(snapshot, shouldUpdatePageStatus);
        }
    }

    private LinkProviderOption GetProviderOption(LinkProviderKind provider)
    {
        return ProviderOptions.FirstOrDefault(item => item.Value == provider) ?? ProviderOptions[0];
    }

    private LinkLatencyOption GetLatencyOption(LinkLatencyMode latencyMode)
    {
        return LatencyOptions.FirstOrDefault(item => item.Value == latencyMode) ?? LatencyOptions[0];
    }

    private bool TryBuildJoinPlanFromInvite(out string failureMessage)
    {
        var result = _linkService.ParseInviteCode(InviteCodeInput);
        if (!result.Success || result.Invite is null)
        {
            failureMessage = result.Message;
            return false;
        }

        failureMessage = "";
        ParsedInviteSummary = $"邀请码有效。端口：{result.Invite.ServerPort}，网络：{result.Invite.NetworkName}，协议版本：{result.Invite.Version}";
        UpdateBackendPlan(LinkRoomRole.Joiner, result.Invite);
        return true;
    }

    private void HandleProcessSnapshotChanged(object? sender, LinkProcessSnapshot snapshot)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            ApplyProcessSnapshot(snapshot);
            return;
        }

        _dispatcher.Invoke(() => ApplyProcessSnapshot(snapshot));
    }

    private void ApplyProcessSnapshot(LinkProcessSnapshot snapshot, bool updateStatusMessage = true)
    {
        LinkProcessStatusText = snapshot.ProcessId is null
            ? snapshot.Message
            : snapshot.Message + " PID：" + snapshot.ProcessId;
        if (!string.IsNullOrWhiteSpace(snapshot.CommandPreview))
        {
            LinkProcessStatusText += Environment.NewLine + snapshot.CommandPreview;
        }

        LinkConnectionStatusText = snapshot.ConnectionStatus;
        LinkConnectedPeersText = snapshot.ConnectedPeers.Count == 0
            ? "暂无已连接节点。"
            : "节点地址：" + string.Join(Environment.NewLine, snapshot.ConnectedPeers);
        LinkProcessLogText = snapshot.RecentLogLines.Count == 0
            ? "暂无联机后端输出。"
            : string.Join(Environment.NewLine, snapshot.RecentLogLines);
        if (updateStatusMessage)
        {
            StatusMessage = snapshot.Message;
        }
    }

    private void ApplyProcessStatus(string message)
    {
        LinkProcessStatusText = message;
        LinkConnectionStatusText = "联机后端未运行。";
        LinkConnectedPeersText = "暂无已连接节点。";
        StatusMessage = message;
    }

    private void CopyText(string text, string displayName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = $"暂无可复制的{displayName}。";
            return;
        }

        if (_clipboard is null)
        {
            StatusMessage = "当前环境没有可用的剪贴板服务。";
            return;
        }

        try
        {
            _clipboard.SetText(text);
            StatusMessage = $"{displayName}已复制。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{displayName}复制失败：" + ex.Message;
            _logger.Error(ex, displayName + "复制失败");
        }
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

    private IEnumerable<string> GetBackendSearchRoots()
    {
        var selectedDirectory = Path.GetDirectoryName(GetSelectedExecutablePath().Trim().Trim('"'));
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            yield return selectedDirectory;
        }

        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plain Craft Launcher Sharp");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Terracotta");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EasyTier");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Terracotta");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EasyTier");
    }
}

public sealed record LinkProviderOption(LinkProviderKind Value, string DisplayName, string Description);

public sealed record LinkLatencyOption(LinkLatencyMode Value, string DisplayName, string Description);
