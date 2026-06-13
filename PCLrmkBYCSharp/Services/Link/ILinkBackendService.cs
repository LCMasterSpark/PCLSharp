using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkBackendService
{
    LinkBackendStatus GetStatus(LinkProviderKind provider, string? executablePath);

    LinkBackendLaunchPlan CreatePlan(
        LinkRoomRole role,
        LinkProviderKind provider,
        LinkInviteInfo invite,
        LinkLatencyMode latencyMode,
        string? customPeer,
        string? executablePath);
}
