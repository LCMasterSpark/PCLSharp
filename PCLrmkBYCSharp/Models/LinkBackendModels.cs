namespace PCLrmkBYCSharp.Models;

public enum LinkBackendReadiness
{
    Ready,
    MissingExecutable,
    InvalidExecutablePath,
    UnsupportedProvider
}

public sealed record LinkBackendStatus(
    LinkProviderKind Provider,
    LinkBackendReadiness Readiness,
    string DisplayName,
    string ExecutablePath,
    string Message)
{
    public bool CanStart => Readiness == LinkBackendReadiness.Ready;

    public string DiagnosticText => Readiness switch
    {
        LinkBackendReadiness.Ready => "后端可执行文件可用，可以创建房间或加入房间后启动联机后端。",
        LinkBackendReadiness.MissingExecutable when string.IsNullOrWhiteSpace(ExecutablePath) => "请先选择后端可执行文件，或点击自动查找从常见目录中定位 Terracotta / EasyTier。",
        LinkBackendReadiness.MissingExecutable => "当前路径没有找到可执行文件，请重新选择或把后端程序放到启动器、Link、Tools、Terracotta 或 EasyTier 目录。",
        LinkBackendReadiness.InvalidExecutablePath => "后端路径格式无效，请使用文件选择器重新选择 .exe 文件。",
        LinkBackendReadiness.UnsupportedProvider => "该联机方案还没有接入后端启动契约，请先切换到 Terracotta 或 EasyTier。",
        _ => Message
    };
}

public sealed record LinkBackendLaunchPlan(
    LinkRoomRole Role,
    LinkProviderKind Provider,
    string DisplayName,
    string ExecutablePath,
    bool CanStart,
    string BlockReason,
    string ProcessArguments,
    IReadOnlyList<string> PlannedOptions,
    string Summary);

public sealed record LinkPortAllocation(
    int ClientForwardPort,
    int RpcPortalPort,
    int ListenersPort);

public enum LinkProcessState
{
    Stopped,
    Running,
    Failed
}

public sealed record LinkProcessSnapshot(
    LinkProcessState State,
    int? ProcessId,
    string Message,
    string CommandPreview,
    string LogFilePath,
    IReadOnlyList<string> RecentLogLines,
    int ConnectedPeerCount,
    IReadOnlyList<string> ConnectedPeers,
    string ConnectionStatus);
