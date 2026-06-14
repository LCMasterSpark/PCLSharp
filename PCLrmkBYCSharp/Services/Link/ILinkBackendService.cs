using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkBackendService
{
    LinkBackendStatus GetStatus(LinkProviderKind provider, string? executablePath);

    string? FindExecutable(LinkProviderKind provider, IEnumerable<string> searchRoots);

    LinkBackendLaunchPlan CreatePlan(
        LinkRoomRole role,
        LinkProviderKind provider,
        LinkInviteInfo invite,
        LinkLatencyMode latencyMode,
        string? customPeer,
        string? executablePath);
}
