using System.IO;
using System.Text;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkProcessService : ILinkProcessService
{
    private const int MaxRecentLogLines = 10;
    private readonly ILinkProcessRunner _runner;
    private readonly IAppLoggerService _logger;
    private readonly IAppPathService? _paths;
    private readonly object _linkLogLock = new();
    private readonly Queue<string> _recentLogLines = new();
    private readonly List<string> _connectedPeers = new();
    private ILinkProcessHandle? _process;
    private string? _linkLogFilePath;
    private int _connectedPeerCount;

    public LinkProcessService(ILinkProcessRunner runner, IAppLoggerService logger, IAppPathService? paths = null)
    {
        _runner = runner;
        _logger = logger;
        _paths = paths;
        Current = CreateSnapshot(LinkProcessState.Stopped, null, "联机后端未启动。", "");
    }

    public event EventHandler<LinkProcessSnapshot>? SnapshotChanged;

    public LinkProcessSnapshot Current { get; private set; }

    public LinkProcessSnapshot Start(LinkBackendLaunchPlan plan)
    {
        if (!plan.CanStart)
        {
            return Publish(LinkProcessState.Failed, null, plan.BlockReason, BuildCommandPreview(plan));
        }

        if (_process is not null && !_process.HasExited)
        {
            return Publish(LinkProcessState.Running, _process.Id, "联机后端已在运行。", BuildCommandPreview(plan));
        }

        try
        {
            _recentLogLines.Clear();
            ResetConnections();
            PrepareLinkLogFile(plan);
            var startInfo = LinkProcessRunner.CreateStartInfo(plan.ExecutablePath, plan.ProcessArguments);
            _process = _runner.Start(startInfo);
            _process.OutputReceived += HandleOutputReceived;
            _process.Exited += HandleProcessExited;
            if (_process.HasExited)
            {
                return PublishExited(_process.ExitCode);
            }

            _logger.Info("联机后端已启动：" + BuildCommandPreview(plan));
            WriteLinkLog("联机后端已启动，PID：" + _process.Id);
            return Publish(LinkProcessState.Running, _process.Id, "联机后端已启动。", BuildCommandPreview(plan));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "启动联机后端失败");
            WriteLinkLog("启动联机后端失败：" + ex.Message);
            return Publish(LinkProcessState.Failed, null, "启动联机后端失败：" + ex.Message, BuildCommandPreview(plan));
        }
    }

    public LinkProcessSnapshot Stop()
    {
        if (_process is null || _process.HasExited)
        {
            _process = null;
            ResetConnections();
            return Publish(LinkProcessState.Stopped, null, "联机后端未运行。", Current.CommandPreview);
        }

        var process = _process;
        try
        {
            var processId = process.Id;
            process.OutputReceived -= HandleOutputReceived;
            process.Exited -= HandleProcessExited;
            process.Stop();
            _process = null;
            ResetConnections();
            _logger.Info("联机后端已停止，PID：" + processId);
            WriteLinkLog("联机后端已停止，PID：" + processId);
            return Publish(LinkProcessState.Stopped, null, "联机后端已停止。", Current.CommandPreview);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "停止联机后端失败");
            WriteLinkLog("停止联机后端失败：" + ex.Message);
            return Publish(LinkProcessState.Failed, process.Id, "停止联机后端失败：" + ex.Message, Current.CommandPreview);
        }
    }

    private void HandleProcessExited(object? sender, LinkProcessExitedEventArgs args)
    {
        if (!ReferenceEquals(sender, _process))
        {
            return;
        }

        PublishExited(args.ExitCode);
    }

    private LinkProcessSnapshot PublishExited(int? exitCode)
    {
        var process = _process;
        if (process is not null)
        {
            process.OutputReceived -= HandleOutputReceived;
            process.Exited -= HandleProcessExited;
        }

        _process = null;
        ResetConnections();
        var exitCodeText = exitCode is null ? "未知" : exitCode.Value.ToString();
        var message = exitCode == 0
            ? $"联机后端已退出，退出码：{exitCodeText}。"
            : $"联机后端异常退出，退出码：{exitCodeText}。";
        if (exitCode == 0)
        {
            _logger.Info(message);
            WriteLinkLog(message);
            return Publish(LinkProcessState.Stopped, null, message, Current.CommandPreview);
        }

        _logger.Warn(message);
        WriteLinkLog(message);
        return Publish(LinkProcessState.Failed, null, message, Current.CommandPreview);
    }

    private void HandleOutputReceived(object? sender, LinkProcessOutputEventArgs args)
    {
        var prefix = args.IsError ? "[ERR] " : "[OUT] ";
        var line = prefix + MaskSecret(args.Line);
        _recentLogLines.Enqueue(line);
        while (_recentLogLines.Count > MaxRecentLogLines)
        {
            _recentLogLines.Dequeue();
        }

        if (args.IsError)
        {
            _logger.Warn("联机后端：" + line);
        }
        else
        {
            _logger.Info("联机后端：" + line);
        }

        WriteLinkLog(line);
        UpdateConnectionCount(args.Line);
        var message = BuildLogMessage(args.Line, args.IsError);
        Publish(LinkProcessState.Running, _process?.Id, message, Current.CommandPreview);
    }

    private LinkProcessSnapshot Publish(LinkProcessState state, int? processId, string message, string commandPreview)
    {
        Current = CreateSnapshot(state, processId, message, commandPreview);
        SnapshotChanged?.Invoke(this, Current);
        return Current;
    }

    private LinkProcessSnapshot CreateSnapshot(LinkProcessState state, int? processId, string message, string commandPreview)
    {
        return new LinkProcessSnapshot(
            state,
            processId,
            message,
            commandPreview,
            _recentLogLines.ToArray(),
            _connectedPeerCount,
            _connectedPeers.ToArray(),
            BuildConnectionStatus(state));
    }

    private static string BuildCommandPreview(LinkBackendLaunchPlan plan)
    {
        return $"\"{plan.ExecutablePath}\" " + MaskSecret(plan.ProcessArguments);
    }

    private void PrepareLinkLogFile(LinkBackendLaunchPlan plan)
    {
        if (_paths is null)
        {
            _linkLogFilePath = null;
            return;
        }

        try
        {
            _paths.EnsureCreated();
            _linkLogFilePath = Path.Combine(_paths.LogsDirectory, $"link-backend-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            WriteLinkLog("联机后端日志已创建。");
            WriteLinkLog("启动命令：" + BuildCommandPreview(plan));
        }
        catch (Exception ex)
        {
            _linkLogFilePath = null;
            _logger.Warn("创建联机后端独立日志失败：" + ex.Message);
        }
    }

    private void WriteLinkLog(string line)
    {
        if (string.IsNullOrWhiteSpace(_linkLogFilePath))
        {
            return;
        }

        try
        {
            lock (_linkLogLock)
            {
                File.AppendAllText(_linkLogFilePath, $"{DateTime.Now:O} {line}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("写入联机后端独立日志失败：" + ex.Message);
            _linkLogFilePath = null;
        }
    }

    private static string BuildLogMessage(string line, bool isError)
    {
        if (line.Contains("new peer connection added", StringComparison.OrdinalIgnoreCase))
        {
            return "已建立联机节点连接。";
        }

        if (line.Contains("peer connection removed", StringComparison.OrdinalIgnoreCase))
        {
            return "联机节点连接已断开。";
        }

        return isError ? "联机后端输出错误日志。" : "联机后端输出日志。";
    }

    private void UpdateConnectionCount(string line)
    {
        if (line.Contains("new peer connection added", StringComparison.OrdinalIgnoreCase))
        {
            var remoteAddress = TryExtractRemoteAddress(line);
            var isNewPeer = remoteAddress is null || !_connectedPeers.Any(peer => string.Equals(peer, remoteAddress, StringComparison.OrdinalIgnoreCase));
            if (isNewPeer)
            {
                _connectedPeerCount++;
            }

            if (remoteAddress is not null && isNewPeer)
            {
                _connectedPeers.Add(remoteAddress);
            }

            return;
        }

        if (line.Contains("peer connection removed", StringComparison.OrdinalIgnoreCase))
        {
            var remoteAddress = TryExtractRemoteAddress(line);
            if (remoteAddress is not null)
            {
                _connectedPeers.RemoveAll(peer => string.Equals(peer, remoteAddress, StringComparison.OrdinalIgnoreCase));
            }

            _connectedPeerCount = Math.Max(_connectedPeers.Count, _connectedPeerCount - 1);
        }
    }

    private string BuildConnectionStatus(LinkProcessState state)
    {
        return state switch
        {
            LinkProcessState.Running when _connectedPeerCount > 0 => $"已连接节点：{_connectedPeerCount} 个{BuildPeerSummary()}",
            LinkProcessState.Running => "等待联机节点连接。",
            LinkProcessState.Failed => "联机后端异常，连接已中断。",
            _ => "联机后端未运行。"
        };
    }

    private void ResetConnections()
    {
        _connectedPeerCount = 0;
        _connectedPeers.Clear();
    }

    private string BuildPeerSummary()
    {
        if (_connectedPeers.Count == 0)
        {
            return "";
        }

        var visiblePeers = _connectedPeers.Take(3).ToArray();
        var suffix = _connectedPeers.Count > visiblePeers.Length ? $" 等 {_connectedPeers.Count} 个地址" : "";
        return "（" + string.Join(", ", visiblePeers) + suffix + "）";
    }

    private static string? TryExtractRemoteAddress(string line)
    {
        var markerIndex = line.IndexOf("remote_addr", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var valueStart = line.IndexOfAny(['=', ':'], markerIndex);
        if (valueStart < 0)
        {
            return null;
        }

        valueStart++;
        while (valueStart < line.Length && (char.IsWhiteSpace(line[valueStart]) || line[valueStart] is '"' or '\'' or '{'))
        {
            valueStart++;
        }

        var valueEnd = valueStart;
        while (valueEnd < line.Length && !char.IsWhiteSpace(line[valueEnd]) && line[valueEnd] is not '"' and not '\'' and not ',' and not '}' and not ']')
        {
            valueEnd++;
        }

        return valueEnd > valueStart ? line[valueStart..valueEnd] : null;
    }

    private static string MaskSecret(string arguments)
    {
        var marker = "--network-secret=";
        var index = arguments.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return arguments;
        }

        var start = index + marker.Length;
        var end = arguments.IndexOf(' ', start);
        return end < 0
            ? arguments[..start] + "***"
            : arguments[..start] + "***" + arguments[end..];
    }
}
