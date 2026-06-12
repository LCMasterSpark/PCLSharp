namespace PCLrmkBYCSharp.Services.Loading;

public sealed class LoadingTask<TInput, TOutput> : ILoadingTask
{
    private readonly Func<TInput, CancellationToken, IProgress<double>, Task<TOutput>> _work;
    private readonly TInput _input;

    public LoadingTask(string name, TInput input, Func<TInput, CancellationToken, IProgress<double>, Task<TOutput>> work)
    {
        Name = name;
        _input = input;
        _work = work;
    }

    public string Name { get; }

    public LoadState State { get; private set; } = LoadState.Waiting;

    public double Progress { get; private set; }

    public Exception? Exception { get; private set; }

    public TOutput? Result { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (State == LoadState.Running)
        {
            return;
        }

        State = LoadState.Running;
        Exception = null;
        Progress = 0;

        try
        {
            var progress = new InlineProgress(value => Progress = Math.Clamp(value, 0, 1));
            Result = await _work(_input, cancellationToken, progress);
            cancellationToken.ThrowIfCancellationRequested();
            Progress = 1;
            State = LoadState.Succeeded;
        }
        catch (OperationCanceledException)
        {
            State = LoadState.Canceled;
            throw;
        }
        catch (Exception ex)
        {
            Exception = ex;
            State = LoadState.Failed;
            throw;
        }
    }

    private sealed class InlineProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }
}
