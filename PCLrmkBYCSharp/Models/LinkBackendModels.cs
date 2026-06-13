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
    string CommandPreview);
