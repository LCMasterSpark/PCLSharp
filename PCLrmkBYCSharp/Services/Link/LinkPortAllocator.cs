using System.Net;
using System.Net.Sockets;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkPortAllocator : ILinkPortAllocator
{
    private const int MinCandidatePort = 20000;
    private const int MaxCandidatePort = 60000;

    public LinkPortAllocation Allocate(int minecraftPort)
    {
        var reserved = new HashSet<int> { minecraftPort };
        var clientForwardPort = FindSinglePort(reserved);
        reserved.Add(clientForwardPort);

        var rpcPortalPort = FindSinglePort(reserved);
        reserved.Add(rpcPortalPort);

        var listenersPort = FindContiguousPorts(3, reserved);
        reserved.Add(listenersPort);
        reserved.Add(listenersPort + 1);
        reserved.Add(listenersPort + 2);

        return new LinkPortAllocation(clientForwardPort, rpcPortalPort, listenersPort);
    }

    private static int FindSinglePort(IReadOnlySet<int> reserved)
    {
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var candidate = Random.Shared.Next(MinCandidatePort, MaxCandidatePort);
            if (!reserved.Contains(candidate) && CanBind(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("没有找到可用于联机的空闲端口。");
    }

    private static int FindContiguousPorts(int count, IReadOnlySet<int> reserved)
    {
        for (var attempt = 0; attempt < 256; attempt++)
        {
            var candidate = Random.Shared.Next(MinCandidatePort, MaxCandidatePort - count);
            if (Enumerable.Range(candidate, count).Any(reserved.Contains))
            {
                continue;
            }

            if (Enumerable.Range(candidate, count).All(CanBind))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("没有找到可用于联机监听的连续空闲端口。");
    }

    private static bool CanBind(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
