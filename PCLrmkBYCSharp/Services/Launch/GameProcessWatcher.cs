using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed partial class GameProcessWatcher(
    IAppLoggerService logger,
    TimeSpan? earlyExitWindow = null,
    int maxTailLines = 80) : IGameProcessWatcher
{
    private readonly TimeSpan _earlyExitWindow = earlyExitWindow ?? TimeSpan.FromSeconds(8);

    public async Task<GameProcessWatchResult> WatchAsync(Process process, CancellationToken cancellationToken = default)
    {
        var outputTail = new ConcurrentQueue<string>();
        var errorTail = new ConcurrentQueue<string>();
        AttachOutputReaders(process, outputTail, errorTail);
        if (process.HasExited)
        {
            logger.Warn($"游戏进程已退出：{process.ExitCode}");
            return GameProcessWatchResult.Exited(process.ExitCode, outputTail.ToArray(), errorTail.ToArray());
        }

        var waitTask = process.WaitForExitAsync(cancellationToken);
        var completed = await Task.WhenAny(waitTask, Task.Delay(_earlyExitWindow, cancellationToken)).ConfigureAwait(false);
        if (completed == waitTask)
        {
            await waitTask.ConfigureAwait(false);
            logger.Info($"游戏进程退出：{process.ExitCode}");
            return GameProcessWatchResult.Exited(process.ExitCode, outputTail.ToArray(), errorTail.ToArray());
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await waitTask.ConfigureAwait(false);
                logger.Info($"游戏进程退出：{process.ExitCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Error(ex, "监控游戏进程失败");
            }
        }, CancellationToken.None);

        return GameProcessWatchResult.Running(outputTail.ToArray(), errorTail.ToArray());
    }

    private void AttachOutputReaders(Process process, ConcurrentQueue<string> outputTail, ConcurrentQueue<string> errorTail)
    {
        try
        {
            if (process.StartInfo.RedirectStandardOutput)
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        var line = Sanitize(args.Data);
                        EnqueueTail(outputTail, line);
                        logger.Info("游戏输出：" + line);
                    }
                };
                process.BeginOutputReadLine();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            logger.Warn("读取游戏标准输出失败：" + ex.Message);
        }

        try
        {
            if (process.StartInfo.RedirectStandardError)
            {
                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        var line = Sanitize(args.Data);
                        EnqueueTail(errorTail, line);
                        logger.Warn("游戏错误：" + line);
                    }
                };
                process.BeginErrorReadLine();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            logger.Warn("读取游戏错误输出失败：" + ex.Message);
        }
    }

    private void EnqueueTail(ConcurrentQueue<string> queue, string line)
    {
        queue.Enqueue(line);
        while (queue.Count > maxTailLines && queue.TryDequeue(out _))
        {
        }
    }

    private static string Sanitize(string value)
    {
        return AccessTokenRegex().Replace(value, "$1***");
    }

    [GeneratedRegex(@"(--(?:accessToken|access_token|auth_access_token)\s+)(""[^""]*""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();
}
