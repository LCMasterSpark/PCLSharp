using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkProcessService : ILinkProcessService
{
    private const int MaxRecentLogLines = 10;
    private readonly ILinkProcessRunner _runner;
    private readonly IAppLoggerService _logger;
    private readonly Queue<string> _recentLogLines = new();
    private ILinkProcessHandle? _process;

    public LinkProcessService(ILinkProcessRunner runner, IAppLoggerService logger)
    {
        _runner = runner;
        _logger = logger;
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
            var startInfo = LinkProcessRunner.CreateStartInfo(plan.ExecutablePath, plan.ProcessArguments);
            _process = _runner.Start(startInfo);
            _process.OutputReceived += HandleOutputReceived;
            _logger.Info("联机后端已启动：" + BuildCommandPreview(plan));
            return Publish(LinkProcessState.Running, _process.Id, "联机后端已启动。", BuildCommandPreview(plan));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "启动联机后端失败");
            return Publish(LinkProcessState.Failed, null, "启动联机后端失败：" + ex.Message, BuildCommandPreview(plan));
        }
    }

    public LinkProcessSnapshot Stop()
    {
        if (_process is null || _process.HasExited)
        {
            _process = null;
            return Publish(LinkProcessState.Stopped, null, "联机后端未运行。", Current.CommandPreview);
        }

        var process = _process;
        try
        {
            var processId = process.Id;
            process.OutputReceived -= HandleOutputReceived;
            process.Stop();
            _process = null;
            _logger.Info("联机后端已停止，PID：" + processId);
            return Publish(LinkProcessState.Stopped, null, "联机后端已停止。", Current.CommandPreview);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "停止联机后端失败");
            return Publish(LinkProcessState.Failed, process.Id, "停止联机后端失败：" + ex.Message, Current.CommandPreview);
        }
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
        return new LinkProcessSnapshot(state, processId, message, commandPreview, _recentLogLines.ToArray());
    }

    private static string BuildCommandPreview(LinkBackendLaunchPlan plan)
    {
        return $"\"{plan.ExecutablePath}\" " + MaskSecret(plan.ProcessArguments);
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
