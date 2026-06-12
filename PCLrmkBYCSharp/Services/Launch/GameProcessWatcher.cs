using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed partial class GameProcessWatcher(IAppLoggerService logger) : IGameProcessWatcher
{
    public Task WatchAsync(Process process, CancellationToken cancellationToken = default)
    {
        AttachOutputReaders(process);
        if (process.HasExited)
        {
            logger.Warn($"游戏进程已退出：{process.ExitCode}");
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                logger.Info($"游戏进程退出：{process.ExitCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Error(ex, "监控游戏进程失败");
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private void AttachOutputReaders(Process process)
    {
        try
        {
            if (process.StartInfo.RedirectStandardOutput)
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        logger.Info("游戏输出：" + Sanitize(args.Data));
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
                        logger.Warn("游戏错误：" + Sanitize(args.Data));
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

    private static string Sanitize(string value)
    {
        return AccessTokenRegex().Replace(value, "$1***");
    }

    [GeneratedRegex(@"(--(?:accessToken|access_token|auth_access_token)\s+)(""[^""]*""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();
}
