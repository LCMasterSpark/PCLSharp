using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class DownloadManagerService(
    IDownloadByteClient client,
    IFileCheckService checker,
    IAppLoggerService logger,
    IAppSettingsService? settings = null) : IDownloadManagerService
{
    private readonly ConcurrentDictionary<string, DownloadTaskSnapshot> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DownloadTaskContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<DownloadTaskSnapshot>? SnapshotChanged;

    public IReadOnlyList<DownloadTaskSnapshot> Tasks => _tasks.Values.OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<DownloadTaskSnapshot> DownloadAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken = default)
    {
        var distinctFiles = files.DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase).ToList();
        using var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _contexts[name] = new DownloadTaskContext(distinctFiles, taskCancellation);
        var finished = 0;
        long received = 0;
        Publish(name, DownloadTaskState.Running, distinctFiles.Count, finished, received, "正在初始化下载", distinctFiles);
        try
        {
            await Parallel.ForEachAsync(
                distinctFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = GetMaxConcurrency(),
                    CancellationToken = taskCancellation.Token
                },
                async (file, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (file.Check.CanUseExistingFile && checker.Check(file.LocalPath, file.Check) is null)
                    {
                        var currentFinished = Interlocked.Increment(ref finished);
                        Publish(name, DownloadTaskState.Running, distinctFiles.Count, currentFinished, Interlocked.Read(ref received), "已存在：" + file.LocalName, distinctFiles);
                        return;
                    }

                    var length = await DownloadFileAsync(
                        file,
                        bytes =>
                        {
                            Interlocked.Add(ref received, bytes);
                            Publish(name, DownloadTaskState.Running, distinctFiles.Count, Volatile.Read(ref finished), Interlocked.Read(ref received), "正在下载：" + file.LocalName, distinctFiles);
                        },
                        token).ConfigureAwait(false);
                    var error = checker.Check(file.LocalPath, file.Check);
                    if (error is not null)
                    {
                        throw new InvalidDataException($"{file.LocalName} 校验失败：{error}");
                    }

                    var completed = Interlocked.Increment(ref finished);
                    Publish(name, DownloadTaskState.Running, distinctFiles.Count, completed, Interlocked.Read(ref received), "已完成：" + file.LocalName, distinctFiles);
                }).ConfigureAwait(false);

            return Publish(name, DownloadTaskState.Succeeded, distinctFiles.Count, finished, received, "下载完成", distinctFiles);
        }
        catch (OperationCanceledException)
        {
            return Publish(name, DownloadTaskState.Canceled, distinctFiles.Count, finished, received, "下载已取消", distinctFiles);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "下载任务失败：" + name);
            return Publish(name, DownloadTaskState.Failed, distinctFiles.Count, finished, received, ex.Message, distinctFiles);
        }
        finally
        {
            if (_contexts.TryGetValue(name, out var context) && ReferenceEquals(context.Cancellation, taskCancellation))
            {
                _contexts[name] = context with { Cancellation = null };
            }
        }
    }

    public bool Cancel(string name)
    {
        if (_contexts.TryGetValue(name, out var context) && context.Cancellation is not null)
        {
            context.Cancellation.Cancel();
            return true;
        }

        return false;
    }

    public int CancelAllRunning()
    {
        var count = 0;
        foreach (var task in Tasks.Where(task => task.State == DownloadTaskState.Running))
        {
            if (Cancel(task.Name))
            {
                count++;
            }
        }

        return count;
    }

    public Task<DownloadTaskSnapshot?> RetryAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_contexts.TryGetValue(name, out var context) || context.Files.Count == 0)
        {
            return Task.FromResult<DownloadTaskSnapshot?>(null);
        }

        return RetryKnownTaskAsync(name, context.Files, cancellationToken);
    }

    public int ClearFinished()
    {
        var count = 0;
        foreach (var task in Tasks.Where(task => task.State is DownloadTaskState.Succeeded or DownloadTaskState.Failed or DownloadTaskState.Canceled))
        {
            if (_tasks.TryRemove(task.Name, out _))
            {
                _contexts.TryRemove(task.Name, out _);
                count++;
            }
        }

        return count;
    }

    private async Task<DownloadTaskSnapshot?> RetryKnownTaskAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken)
    {
        return await DownloadAsync(name, files, cancellationToken).ConfigureAwait(false);
    }

    public long GetSpeedLimitBytesPerSecond()
    {
        var value = Math.Clamp(settings?.Get(AppSettingKeys.ToolDownloadSpeed, 42) ?? 42, 0, 42);
        if (value <= 14)
        {
            return (long)((value + 1) * 0.1 * 1024 * 1024);
        }

        if (value <= 31)
        {
            return (long)((value - 11) * 0.5 * 1024 * 1024);
        }

        return value <= 41
            ? (value - 21L) * 1024 * 1024
            : -1;
    }

    private async Task<long> DownloadFileAsync(DownloadFile file, Action<long> reportBytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(file.LocalPath)!;
        Directory.CreateDirectory(directory);
        var tempPath = GetTempPath(file.LocalPath);
        if (File.Exists(tempPath) && checker.Check(tempPath, file.Check) is null)
        {
            File.Move(tempPath, file.LocalPath, overwrite: true);
            return 0;
        }

        Exception? last = null;
        foreach (var source in file.Sources)
        {
            try
            {
                long lastProgress = 0;
                var progress = new InlineProgress(total =>
                {
                    var delta = total - lastProgress;
                    if (delta <= 0)
                    {
                        return;
                    }

                    lastProgress = total;
                    reportBytes(delta);
                });
                var length = await client.DownloadToFileAsync(source, tempPath, file.SimulateBrowserHeaders, progress, cancellationToken).ConfigureAwait(false);
                var missingProgress = length - lastProgress;
                if (missingProgress > 0)
                {
                    reportBytes(missingProgress);
                }
                await ApplySpeedLimitAsync(length, cancellationToken).ConfigureAwait(false);
                var error = checker.Check(tempPath, file.Check);
                if (error is not null)
                {
                    TryDelete(tempPath);
                    throw new InvalidDataException($"{file.LocalName} 校验失败：{error}");
                }

                File.Move(tempPath, file.LocalPath, overwrite: true);
                return length;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                logger.Warn($"下载源失败：{source}，{ex.Message}");
            }
        }

        throw new HttpRequestException("所有下载源均失败：" + file.LocalName, last);
    }

    private int GetMaxConcurrency()
    {
        return Math.Clamp((settings?.Get(AppSettingKeys.ToolDownloadThread, 63) ?? 63) + 1, 1, 256);
    }

    private async Task ApplySpeedLimitAsync(long bytes, CancellationToken cancellationToken)
    {
        var speedLimit = GetSpeedLimitBytesPerSecond();
        if (speedLimit <= 0 || bytes <= 0)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(bytes / (double)speedLimit);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private DownloadTaskSnapshot Publish(string name, DownloadTaskState state, int totalFiles, int finishedFiles, long bytesReceived, string message, IReadOnlyList<DownloadFile> files)
    {
        var progress = totalFiles == 0 ? 1 : Math.Clamp((double)finishedFiles / totalFiles, 0, 1);
        var snapshot = new DownloadTaskSnapshot(name, state, totalFiles, finishedFiles, bytesReceived, progress, message)
        {
            CanCancel = state == DownloadTaskState.Running,
            CanRetry = state is DownloadTaskState.Failed or DownloadTaskState.Canceled,
            PrimaryLocalPath = files.FirstOrDefault()?.LocalPath ?? "",
            LocalPaths = files.Select(file => file.LocalPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToList()
        };
        _tasks[name] = snapshot;
        PublishSnapshotChanged(snapshot);
        return snapshot;
    }

    private void PublishSnapshotChanged(DownloadTaskSnapshot snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<DownloadTaskSnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "下载任务快照订阅者处理失败：" + snapshot.Name);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string GetTempPath(string localPath)
    {
        return localPath + ".pcldownload";
    }

    private sealed record DownloadTaskContext(
        IReadOnlyList<DownloadFile> Files,
        CancellationTokenSource? Cancellation);

    private sealed class InlineProgress(Action<long> handler) : IProgress<long>
    {
        public void Report(long value)
        {
            handler(value);
        }
    }
}
