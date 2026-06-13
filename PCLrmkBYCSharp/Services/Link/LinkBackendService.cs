using System.IO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkBackendService : ILinkBackendService
{
    public LinkBackendStatus GetStatus(LinkProviderKind provider, string? executablePath)
    {
        var displayName = GetDisplayName(provider);
        if (provider is not (LinkProviderKind.Terracotta or LinkProviderKind.EasyTier))
        {
            return new LinkBackendStatus(provider, LinkBackendReadiness.UnsupportedProvider, displayName, "", "暂不支持该联机方案。");
        }

        var normalizedPath = NormalizeExecutablePath(executablePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return new LinkBackendStatus(provider, LinkBackendReadiness.MissingExecutable, displayName, "", displayName + " 后端尚未配置。");
        }

        try
        {
            normalizedPath = Path.GetFullPath(normalizedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LinkBackendStatus(provider, LinkBackendReadiness.InvalidExecutablePath, displayName, normalizedPath, "联机后端路径无效。");
        }

        if (!File.Exists(normalizedPath))
        {
            return new LinkBackendStatus(provider, LinkBackendReadiness.MissingExecutable, displayName, normalizedPath, "未找到联机后端可执行文件。");
        }

        return new LinkBackendStatus(provider, LinkBackendReadiness.Ready, displayName, normalizedPath, displayName + " 后端已就绪。");
    }

    public LinkBackendLaunchPlan CreatePlan(
        LinkRoomRole role,
        LinkProviderKind provider,
        LinkInviteInfo invite,
        LinkLatencyMode latencyMode,
        string? customPeer,
        string? executablePath)
    {
        var status = GetStatus(provider, executablePath);
        var options = BuildPlannedOptions(role, invite, latencyMode, customPeer);
        var arguments = BuildProcessArguments(role, invite, latencyMode, customPeer);
        var roleText = role == LinkRoomRole.Host ? "创建房间" : "加入房间";
        var summary = $"{status.DisplayName} / {roleText} / 端口 {invite.ServerPort} / 网络 {invite.NetworkName}";
        return new LinkBackendLaunchPlan(
            role,
            provider,
            status.DisplayName,
            status.ExecutablePath,
            status.CanStart,
            status.CanStart ? "" : status.Message,
            arguments,
            options,
            summary);
    }

    private static IReadOnlyList<string> BuildPlannedOptions(LinkRoomRole role, LinkInviteInfo invite, LinkLatencyMode latencyMode, string? customPeer)
    {
        var peers = SplitPeers(customPeer).ToArray();
        var options = new List<string>
        {
            "role=" + (role == LinkRoomRole.Host ? "host" : "join"),
            "minecraft-port=" + invite.ServerPort,
            "network-name=" + invite.NetworkName,
            "network-secret=***",
            "latency-mode=" + (latencyMode == LinkLatencyMode.DirectFirst ? "direct-first" : "latency-first")
        };

        if (invite.DiscoverNodeId > 0)
        {
            options.Add("discover-node=" + invite.DiscoverNodeId);
        }

        if (peers.Length > 0)
        {
            options.Add("custom-peer-count=" + peers.Length);
            options.AddRange(peers.Select(peer => "custom-peer=" + peer));
        }

        return options;
    }

    private static string BuildProcessArguments(LinkRoomRole role, LinkInviteInfo invite, LinkLatencyMode latencyMode, string? customPeer)
    {
        var arguments = new List<string>
        {
            "--network-name=" + QuoteArgumentValue(invite.NetworkName),
            "--network-secret=" + QuoteArgumentValue(invite.NetworkSecret),
            "--private-mode",
            "true",
            "--tcp-whitelist=" + invite.ServerPort,
            "--udp-whitelist=" + invite.ServerPort
        };

        if (role == LinkRoomRole.Host)
        {
            arguments.Add("-i");
            arguments.Add("10.114.114.114");
            arguments.Add("--hostname=" + QuoteArgumentValue("PCLSharp-Host"));
        }
        else
        {
            arguments.Add("-d");
            arguments.Add("--hostname=" + QuoteArgumentValue("PCLSharp-Client"));
        }

        if (latencyMode == LinkLatencyMode.LatencyFirst)
        {
            arguments.Add("--latency-first");
        }

        foreach (var peer in SplitPeers(customPeer))
        {
            arguments.Add("-p");
            arguments.Add(QuoteArgumentValue(peer));
        }

        return string.Join(" ", arguments);
    }

    private static IEnumerable<string> SplitPeers(string? customPeer)
    {
        return (customPeer ?? "")
            .Split(['，', ',', '\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string QuoteArgumentValue(string value)
    {
        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string GetDisplayName(LinkProviderKind provider)
    {
        return provider switch
        {
            LinkProviderKind.Terracotta => "陶瓦联机 Terracotta",
            LinkProviderKind.EasyTier => "EasyTier",
            _ => provider.ToString()
        };
    }

    private static string NormalizeExecutablePath(string? executablePath)
    {
        return (executablePath ?? "").Trim().Trim('"');
    }
}
