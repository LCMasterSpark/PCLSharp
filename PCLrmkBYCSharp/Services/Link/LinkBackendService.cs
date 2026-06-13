using System.IO;
using System.Net;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkBackendService : ILinkBackendService
{
    private const string HostVirtualAddress = "10.114.114.114";
    private readonly ILinkPortAllocator _portAllocator;

    public LinkBackendService(ILinkPortAllocator? portAllocator = null)
    {
        _portAllocator = portAllocator ?? new LinkPortAllocator();
    }

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
        var portError = "";
        var ports = new LinkPortAllocation(0, 0, 0);
        if (status.CanStart)
        {
            try
            {
                ports = _portAllocator.Allocate(invite.ServerPort);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                portError = "联机端口分配失败：" + ex.Message;
            }
        }

        var canStart = status.CanStart && string.IsNullOrWhiteSpace(portError);
        var options = BuildPlannedOptions(role, invite, latencyMode, customPeer, ports);
        if (!string.IsNullOrWhiteSpace(portError))
        {
            options = [.. options, "port-allocation=failed", "port-error=" + portError];
        }

        var arguments = canStart ? BuildProcessArguments(role, invite, latencyMode, customPeer, ports) : "";
        var roleText = role == LinkRoomRole.Host ? "创建房间" : "加入房间";
        var summary = $"{status.DisplayName} / {roleText} / 端口 {invite.ServerPort} / 网络 {invite.NetworkName}";
        return new LinkBackendLaunchPlan(
            role,
            provider,
            status.DisplayName,
            status.ExecutablePath,
            canStart,
            canStart ? "" : string.IsNullOrWhiteSpace(portError) ? status.Message : portError,
            arguments,
            options,
            summary);
    }

    private static IReadOnlyList<string> BuildPlannedOptions(LinkRoomRole role, LinkInviteInfo invite, LinkLatencyMode latencyMode, string? customPeer, LinkPortAllocation ports)
    {
        var peers = SplitPeers(customPeer).ToArray();
        var tcpWhitelist = role == LinkRoomRole.Host ? invite.ServerPort : 0;
        var udpWhitelist = role == LinkRoomRole.Host ? invite.ServerPort : 0;
        var options = new List<string>
        {
            "role=" + (role == LinkRoomRole.Host ? "host" : "join"),
            "minecraft-port=" + invite.ServerPort,
            "network-name=" + invite.NetworkName,
            "network-secret=***",
            "latency-mode=" + (latencyMode == LinkLatencyMode.DirectFirst ? "direct-first" : "latency-first"),
            "tcp-whitelist=" + tcpWhitelist,
            "udp-whitelist=" + udpWhitelist
        };

        if (ports.ListenersPort > 0)
        {
            options.Add("listeners-port=" + ports.ListenersPort);
        }

        if (ports.RpcPortalPort > 0)
        {
            options.Add("rpc-portal-port=" + ports.RpcPortalPort);
        }

        if (invite.DiscoverNodeId > 0)
        {
            options.Add("discover-node=" + invite.DiscoverNodeId);
        }

        if (peers.Length > 0)
        {
            options.Add("custom-peer-count=" + peers.Length);
            options.AddRange(peers.Select(peer => "custom-peer=" + peer));
        }

        if (role == LinkRoomRole.Joiner && ports.ClientForwardPort > 0)
        {
            options.Add("client-forward-port=" + ports.ClientForwardPort);
            options.AddRange(BuildPortForwardSpecs(invite.ServerPort, ports.ClientForwardPort).Select(forward => "port-forward=" + forward));
        }

        return options;
    }

    private static string BuildProcessArguments(LinkRoomRole role, LinkInviteInfo invite, LinkLatencyMode latencyMode, string? customPeer, LinkPortAllocation ports)
    {
        var arguments = new List<string>
        {
            "--network-name=" + QuoteArgumentValue(invite.NetworkName),
            "--network-secret=" + QuoteArgumentValue(invite.NetworkSecret),
            "--listeners",
            ports.ListenersPort.ToString(),
            "--rpc-portal",
            ports.RpcPortalPort.ToString(),
            "--private-mode",
            "true"
        };

        if (role == LinkRoomRole.Host)
        {
            arguments.Add("-i");
            arguments.Add(HostVirtualAddress);
            arguments.Add("--hostname=" + QuoteArgumentValue("PCLSharp-Host"));
            arguments.Add("--tcp-whitelist=" + invite.ServerPort);
            arguments.Add("--udp-whitelist=" + invite.ServerPort);
        }
        else
        {
            arguments.Add("-d");
            arguments.Add("--hostname=" + QuoteArgumentValue("PCLSharp-Client"));
            arguments.Add("--tcp-whitelist=0");
            arguments.Add("--udp-whitelist=0");
            foreach (var forward in BuildPortForwardSpecs(invite.ServerPort, ports.ClientForwardPort))
            {
                arguments.Add("--port-forward");
                arguments.Add(QuoteArgumentValue(forward));
            }
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

    private static IReadOnlyList<string> BuildPortForwardSpecs(int serverPort, int clientForwardPort)
    {
        var ipv6Loopback = IPAddress.IPv6Loopback.ToString();
        var ipv4Loopback = IPAddress.Loopback.ToString();
        return
        [
            $"tcp://[{ipv6Loopback}]:{clientForwardPort}/{HostVirtualAddress}:{serverPort}",
            $"udp://[{ipv6Loopback}]:{clientForwardPort}/{HostVirtualAddress}:{serverPort}",
            $"tcp://{ipv4Loopback}:{clientForwardPort}/{HostVirtualAddress}:{serverPort}",
            $"udp://{ipv4Loopback}:{clientForwardPort}/{HostVirtualAddress}:{serverPort}"
        ];
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
