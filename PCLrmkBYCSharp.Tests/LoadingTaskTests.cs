using PCLrmkBYCSharp.Services.Loading;

namespace PCLrmkBYCSharp.Tests;

public sealed class LoadingTaskTests
{
    [Fact]
    public async Task RunAsyncMovesWaitingToSucceeded()
    {
        var task = new LoadingTask<string, string>(
            "success",
            "ok",
            (input, _, progress) =>
            {
                progress.Report(0.5);
                return Task.FromResult(input);
            });

        await task.RunAsync();

        Assert.Equal(LoadState.Succeeded, task.State);
        Assert.Equal("ok", task.Result);
        Assert.Equal(1, task.Progress);
    }

    [Fact]
    public async Task RunAsyncMovesToCanceled()
    {
        using var cts = new CancellationTokenSource();
        var task = new LoadingTask<int, int>(
            "cancel",
            1,
            (_, cancellationToken, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(1);
            });

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task.RunAsync(cts.Token));
        Assert.Equal(LoadState.Canceled, task.State);
    }

    [Fact]
    public async Task RunAsyncMovesToFailedAndStoresException()
    {
        var task = new LoadingTask<int, int>(
            "fail",
            1,
            (_, _, _) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => task.RunAsync());

        Assert.Equal(LoadState.Failed, task.State);
        Assert.IsType<InvalidOperationException>(task.Exception);
    }

    [Fact]
    public async Task LoadingGroupRunsTasksInOrder()
    {
        var order = new List<int>();
        var first = new LoadingTask<int, int>("first", 1, (input, _, _) =>
        {
            order.Add(input);
            return Task.FromResult(input);
        });
        var second = new LoadingTask<int, int>("second", 2, (input, _, _) =>
        {
            order.Add(input);
            return Task.FromResult(input);
        });
        var group = new LoadingGroup("group", [first, second]);

        await group.RunAsync();

        Assert.Equal(LoadState.Succeeded, group.State);
        Assert.Equal([1, 2], order);
    }
}
