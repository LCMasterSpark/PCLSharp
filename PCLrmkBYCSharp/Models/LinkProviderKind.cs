namespace PCLrmkBYCSharp.Models;

public enum LinkProviderKind
{
    Terracotta,
    EasyTier
}

public enum LinkLatencyMode
{
    DirectFirst,
    LatencyFirst
}

public enum LinkRoomRole
{
    Host,
    Joiner
}

public sealed record LinkInviteInfo(
    int ServerPort,
    string NetworkName,
    string NetworkSecret,
    int Version,
    int DiscoverNodeId);
