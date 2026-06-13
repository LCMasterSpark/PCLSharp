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
    IReadOnlyList<string> PlannedOptions,
    string Summary);
