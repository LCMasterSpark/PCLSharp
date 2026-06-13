using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkProcessService : ILinkProcessService
{
    private readonly ILinkProcessRunner _runner;
    private readonly IAppLoggerService _logger;
    private ILinkProcessHandle? _process;

    public LinkProcessService(ILinkProcessRunner runner, IAppLoggerService logger)
    {
        _runner = runner;
        _logger = logger;
        Current = new LinkProcessSnapshot(LinkProcessState.Stopped, null, "联机后端未启动。", "");
    }

    public LinkProcessSnapshot Current { get; private set; }

    public LinkProcessSnapshot Start(LinkBackendLaunchPlan plan)
    {
        if (!plan.CanStart)
        {
            Current = new LinkProcessSnapshot(LinkProcessState.Failed, null, plan.BlockReason, BuildCommandPreview(plan));
            return Current;
        }

        if (_process is not null && !_process.HasExited)
        {
            Current = new LinkProcessSnapshot(LinkProcessState.Running, _process.Id, "联机后端已在运行。", BuildCommandPreview(plan));
            return Current;
        }

        try
        {
            var startInfo = LinkProcessRunner.CreateStartInfo(plan.ExecutablePath, plan.ProcessArguments);
            _process = _runner.Start(startInfo);
            Current = new LinkProcessSnapshot(LinkProcessState.Running, _process.Id, "联机后端已启动。", BuildCommandPreview(plan));
            _logger.Info("联机后端已启动：" + BuildCommandPreview(plan));
            return Current;
        }
        catch (Exception ex)
        {
            Current = new LinkProcessSnapshot(LinkProcessState.Failed, null, "启动联机后端失败：" + ex.Message, BuildCommandPreview(plan));
            _logger.Error(ex, "启动联机后端失败");
            return Current;
        }
    }

    public LinkProcessSnapshot Stop()
    {
        if (_process is null || _process.HasExited)
        {
            Current = new LinkProcessSnapshot(LinkProcessState.Stopped, null, "联机后端未运行。", Current.CommandPreview);
            _process = null;
            return Current;
        }

        var process = _process;
        try
        {
            var processId = process.Id;
            process.Stop();
            _process = null;
            Current = new LinkProcessSnapshot(LinkProcessState.Stopped, null, "联机后端已停止。", Current.CommandPreview);
            _logger.Info("联机后端已停止，PID：" + processId);
            return Current;
        }
        catch (Exception ex)
        {
            Current = new LinkProcessSnapshot(LinkProcessState.Failed, process.Id, "停止联机后端失败：" + ex.Message, Current.CommandPreview);
            _logger.Error(ex, "停止联机后端失败");
            return Current;
        }
    }

    private static string BuildCommandPreview(LinkBackendLaunchPlan plan)
    {
        return $"\"{plan.ExecutablePath}\" " + MaskSecret(plan.ProcessArguments);
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
