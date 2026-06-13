using PCLrmkBYCSharp.Services.Link;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.ViewModels;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PCLrmkBYCSharp.Tests;

public sealed class PclLinkServiceTests
{
    [Fact]
    public void CreateHostInviteBuildsParseablePclCode()
    {
        var service = new PclLinkService();

        var invite = service.CreateHostInvite(25565);
        var code = service.BuildInviteCode(invite);
        var parsed = service.ParseInviteCode("【" + code + "】");

        Assert.True(parsed.Success);
        Assert.Equal(25565, parsed.Invite?.ServerPort);
        Assert.Equal(2, parsed.Invite?.Version);
        Assert.Contains(code, service.BuildShareText(invite), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseInviteCodeAcceptsLegacyV1Code()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("P04D2-ABCDE-12345");

        Assert.True(result.Success);
        Assert.Equal(1234, result.Invite?.ServerPort);
        Assert.Equal(1, result.Invite?.Version);
        Assert.Equal(0x05E, result.Invite?.DiscoverNodeId);
    }

    [Fact]
    public void ParseInviteCodeNormalizesLookalikeCharacters()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("[P04D2-ABCOI-1OIO1-02000]");

        Assert.True(result.Success);
        Assert.Equal("P04D2-ABC01", result.Invite?.NetworkName);
        Assert.Equal("10101", result.Invite?.NetworkSecret);
    }

    [Fact]
    public void ParseInviteCodeRejectsOtherLauncherRoomCode()
    {
        var service = new PclLinkService();

        var result = service.ParseInviteCode("U/example-room");

        Assert.False(result.Success);
        Assert.Contains("PCL", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateHostInviteRejectsInvalidServerPort()
    {
        var service = new PclLinkService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateHostInvite(80));
    }

    [Fact]
    public void LinkBackendServiceReportsMissingExecutable()
    {
        var service = CreateLinkBackendService();

        var status = service.GetStatus(LinkProviderKind.Terracotta, "");

        Assert.False(status.CanStart);
        Assert.Equal(LinkBackendReadiness.MissingExecutable, status.Readiness);
        Assert.Contains("尚未配置", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkBackendServiceBuildsReadyPlanWithMaskedSecret()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "terracotta.exe");
        File.WriteAllText(executable, "");
        var service = CreateLinkBackendService();
        var invite = new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0);

        var plan = service.CreatePlan(LinkRoomRole.Host, LinkProviderKind.Terracotta, invite, LinkLatencyMode.DirectFirst, "peer.example", executable);

        Assert.True(plan.CanStart);
        Assert.Contains("陶瓦联机", plan.DisplayName, StringComparison.Ordinal);
        Assert.Contains("SECRET", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("network-secret=***", plan.PlannedOptions);
        Assert.DoesNotContain("SECRET", string.Join(" ", plan.PlannedOptions), StringComparison.Ordinal);
        Assert.Contains("custom-peer=peer.example", plan.PlannedOptions);
        Assert.Contains("--tcp-whitelist=25565", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--udp-whitelist=25565", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--listeners 25572", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--rpc-portal 25571", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--port-forward", plan.ProcessArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkBackendServiceBuildsJoinerPortForwardArguments()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "terracotta.exe");
        File.WriteAllText(executable, "");
        var service = CreateLinkBackendService();
        var invite = new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0);

        var plan = service.CreatePlan(LinkRoomRole.Joiner, LinkProviderKind.Terracotta, invite, LinkLatencyMode.DirectFirst, "", executable);

        Assert.True(plan.CanStart);
        Assert.Contains("--tcp-whitelist=0", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--udp-whitelist=0", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--listeners 25572", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--rpc-portal 25571", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--port-forward tcp://[::1]:25570/10.114.114.114:25565", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--port-forward udp://[::1]:25570/10.114.114.114:25565", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--port-forward tcp://127.0.0.1:25570/10.114.114.114:25565", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Contains("--port-forward udp://127.0.0.1:25570/10.114.114.114:25565", plan.ProcessArguments, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(plan.ProcessArguments, "--port-forward "));
        Assert.Contains("client-forward-port=25570", plan.PlannedOptions);
        Assert.Contains("listeners-port=25572", plan.PlannedOptions);
        Assert.Contains("rpc-portal-port=25571", plan.PlannedOptions);
        Assert.Equal(4, plan.PlannedOptions.Count(option => option.StartsWith("port-forward=", StringComparison.Ordinal)));
    }

    [Fact]
    public void LinkBackendServiceSplitsAndDeduplicatesCustomPeers()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "terracotta.exe");
        File.WriteAllText(executable, "");
        var service = CreateLinkBackendService();
        var invite = new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0);
        var peers = "tcp://a.example:11010, tcp://b.example:11010\r\ntcp://a.example:11010；tcp://c.example:11010";

        var plan = service.CreatePlan(LinkRoomRole.Joiner, LinkProviderKind.Terracotta, invite, LinkLatencyMode.DirectFirst, peers, executable);

        Assert.Contains("custom-peer-count=3", plan.PlannedOptions);
        Assert.Contains("custom-peer=tcp://a.example:11010", plan.PlannedOptions);
        Assert.Contains("custom-peer=tcp://b.example:11010", plan.PlannedOptions);
        Assert.Contains("custom-peer=tcp://c.example:11010", plan.PlannedOptions);
        Assert.Equal(3, plan.PlannedOptions.Count(option => option.StartsWith("custom-peer=", StringComparison.Ordinal)));
        Assert.Equal(3, CountOccurrences(plan.ProcessArguments, "-p "));
    }

    [Fact]
    public void LinkPortAllocatorReturnsDistinctTcpAndUdpBindablePorts()
    {
        var allocation = new LinkPortAllocator().Allocate(25565);
        var ports = new[]
        {
            allocation.ClientForwardPort,
            allocation.RpcPortalPort,
            allocation.ListenersPort,
            allocation.ListenersPort + 1,
            allocation.ListenersPort + 2
        };

        Assert.DoesNotContain(25565, ports);
        Assert.Equal(ports.Length, ports.Distinct().Count());
        foreach (var port in ports)
        {
            Assert.True(CanBindTcpAndUdp(port), $"端口 {port} 应该仍可被 TCP 与 UDP 绑定。");
        }
    }

    [Fact]
    public void LinkProcessServiceStartsBackendWithSafeProcessSettings()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "easytier.exe");
        File.WriteAllText(executable, "");
        var backend = CreateLinkBackendService();
        var plan = backend.CreatePlan(
            LinkRoomRole.Host,
            LinkProviderKind.EasyTier,
            new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0),
            LinkLatencyMode.LatencyFirst,
            "",
            executable);
        var runner = new CaptureLinkProcessRunner();
        var service = new LinkProcessService(runner, new NullLoggerService());

        var snapshot = service.Start(plan);

        Assert.Equal(LinkProcessState.Running, snapshot.State);
        Assert.Equal(1234, snapshot.ProcessId);
        Assert.Equal(executable, runner.LastStartInfo?.FileName);
        Assert.Contains("--network-secret=SECRET", runner.LastStartInfo?.Arguments, StringComparison.Ordinal);
        Assert.Contains("--network-secret=***", snapshot.CommandPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", snapshot.CommandPreview, StringComparison.Ordinal);
        Assert.False(runner.LastStartInfo?.UseShellExecute);
        Assert.True(runner.LastStartInfo?.RedirectStandardOutput);
        Assert.True(runner.LastStartInfo?.RedirectStandardError);

        var second = service.Start(plan);

        Assert.Equal(1, runner.StartCount);
        Assert.Equal("联机后端已在运行。", second.Message);
    }

    [Fact]
    public void LinkProcessServiceCapturesRecentOutputAndMasksSecrets()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "easytier.exe");
        File.WriteAllText(executable, "");
        var backend = CreateLinkBackendService();
        var plan = backend.CreatePlan(
            LinkRoomRole.Host,
            LinkProviderKind.EasyTier,
            new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0),
            LinkLatencyMode.DirectFirst,
            "",
            executable);
        var runner = new CaptureLinkProcessRunner();
        var service = new LinkProcessService(runner, new NullLoggerService());
        var snapshots = new List<LinkProcessSnapshot>();
        service.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

        service.Start(plan);
        runner.Handle.Publish("new peer connection added remote_addr=10.0.0.2");
        Assert.Equal(1, service.Current.ConnectedPeerCount);
        Assert.Equal("10.0.0.2", Assert.Single(service.Current.ConnectedPeers));
        Assert.Contains("1", service.Current.ConnectionStatus, StringComparison.Ordinal);
        Assert.Contains("10.0.0.2", service.Current.ConnectionStatus, StringComparison.Ordinal);

        runner.Handle.Publish("new peer connection added remote_addr=10.0.0.2");
        Assert.Equal(1, service.Current.ConnectedPeerCount);
        Assert.Single(service.Current.ConnectedPeers);

        runner.Handle.Publish("--network-secret=SECRET backend warning", isError: true);

        Assert.Contains(snapshots, snapshot => snapshot.Message == "已建立联机节点连接。");
        Assert.Equal("联机后端输出错误日志。", service.Current.Message);
        Assert.Equal(1, service.Current.ConnectedPeerCount);
        Assert.Contains(service.Current.RecentLogLines, line => line.Contains("[OUT] new peer connection added", StringComparison.Ordinal));
        Assert.Contains(service.Current.RecentLogLines, line => line.Contains("[ERR] --network-secret=***", StringComparison.Ordinal));
        Assert.DoesNotContain("SECRET", string.Join(Environment.NewLine, service.Current.RecentLogLines), StringComparison.Ordinal);

        runner.Handle.Publish("peer connection removed remote_addr=10.0.0.2");
        Assert.Equal(0, service.Current.ConnectedPeerCount);
        Assert.Empty(service.Current.ConnectedPeers);
    }

    [Theory]
    [InlineData(0, LinkProcessState.Stopped, "联机后端已退出")]
    [InlineData(7, LinkProcessState.Failed, "联机后端异常退出")]
    public void LinkProcessServiceUpdatesSnapshotWhenBackendExits(int exitCode, LinkProcessState expectedState, string expectedMessage)
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "easytier.exe");
        File.WriteAllText(executable, "");
        var plan = CreateLinkBackendService().CreatePlan(
            LinkRoomRole.Joiner,
            LinkProviderKind.EasyTier,
            new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0),
            LinkLatencyMode.DirectFirst,
            "",
            executable);
        var runner = new CaptureLinkProcessRunner();
        var service = new LinkProcessService(runner, new NullLoggerService());
        var snapshots = new List<LinkProcessSnapshot>();
        service.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

        service.Start(plan);
        runner.Handle.PublishExited(exitCode);

        Assert.Equal(expectedState, service.Current.State);
        Assert.Null(service.Current.ProcessId);
        Assert.Equal(0, service.Current.ConnectedPeerCount);
        Assert.Contains(expectedMessage, service.Current.Message, StringComparison.Ordinal);
        Assert.Contains($"退出码：{exitCode}", service.Current.Message, StringComparison.Ordinal);
        Assert.Contains(snapshots, snapshot => snapshot.State == expectedState && snapshot.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void LinkProcessServiceStopsRunningBackend()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "easytier.exe");
        File.WriteAllText(executable, "");
        var plan = CreateLinkBackendService().CreatePlan(
            LinkRoomRole.Joiner,
            LinkProviderKind.EasyTier,
            new LinkInviteInfo(25565, "P63DD-ABCDE", "SECRET", 2, 0),
            LinkLatencyMode.DirectFirst,
            "",
            executable);
        var runner = new CaptureLinkProcessRunner();
        var service = new LinkProcessService(runner, new NullLoggerService());

        service.Start(plan);
        var snapshot = service.Stop();

        Assert.Equal(LinkProcessState.Stopped, snapshot.State);
        Assert.True(runner.Handle.Stopped);
    }

    [Fact]
    public async Task LinkPageViewModelPicksBackendExecutableAndPersistsPath()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "terracotta.exe");
        File.WriteAllText(executable, "");
        var settings = new AppSettingsService(new TestAppPathService(Path.Combine(temp.Path, "appdata")));
        var viewModel = new LinkPageViewModel(
            new PclLinkService(),
            settings,
            new NullLoggerService(),
            linkBackend: new LinkBackendService(),
            fileDialogs: new ExecutableFileDialogService(executable));

        await viewModel.PickSelectedBackendExecutableCommand.ExecuteAsync(null);

        Assert.Equal(executable, viewModel.TerracottaExecutablePath);
        Assert.Equal(executable, settings.Get(AppSettingKeys.LinkTerracottaExecutablePath, ""));
        Assert.Contains("已就绪", viewModel.BackendStatusText, StringComparison.Ordinal);
    }

    private sealed class ExecutableFileDialogService(string executablePath) : IFileDialogService
    {
        public string? PickFolder(string title, string initialDirectory) => null;

        public string? PickJavaExecutable(string initialDirectory) => null;

        public string? PickExecutable(string title, string initialDirectory, string filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*") => executablePath;

        public string? PickSkinFile(string initialDirectory) => null;

        public string? PickModpackFile(string initialDirectory) => null;

        public IReadOnlyList<string> PickModFiles(string initialDirectory) => [];

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter) => null;
    }

    private static LinkBackendService CreateLinkBackendService()
    {
        return new LinkBackendService(new FixedLinkPortAllocator());
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static bool CanBindTcpAndUdp(int port)
    {
        TcpListener? tcpListener = null;
        UdpClient? udpClient = null;
        try
        {
            tcpListener = new TcpListener(IPAddress.Loopback, port);
            tcpListener.Start();
            udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            tcpListener?.Stop();
            udpClient?.Dispose();
        }
    }

    private sealed class FixedLinkPortAllocator : ILinkPortAllocator
    {
        public LinkPortAllocation Allocate(int minecraftPort)
        {
            return new LinkPortAllocation(25570, 25571, 25572);
        }
    }

    private sealed class CaptureLinkProcessRunner : ILinkProcessRunner
    {
        public CaptureLinkProcessHandle Handle { get; } = new();

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public int StartCount { get; private set; }

        public ILinkProcessHandle Start(ProcessStartInfo startInfo)
        {
            StartCount++;
            LastStartInfo = startInfo;
            return Handle;
        }
    }

    private sealed class CaptureLinkProcessHandle : ILinkProcessHandle
    {
        public event EventHandler<LinkProcessOutputEventArgs>? OutputReceived;

        public event EventHandler<LinkProcessExitedEventArgs>? Exited;

        public int Id => 1234;

        public int? ExitCode { get; private set; }

        public bool HasExited { get; private set; }

        public bool Stopped { get; private set; }

        public void Publish(string line, bool isError = false)
        {
            OutputReceived?.Invoke(this, new LinkProcessOutputEventArgs(line, isError));
        }

        public void PublishExited(int? exitCode)
        {
            ExitCode = exitCode;
            HasExited = true;
            Exited?.Invoke(this, new LinkProcessExitedEventArgs(exitCode));
        }

        public void Stop()
        {
            Stopped = true;
            HasExited = true;
        }
    }
}
