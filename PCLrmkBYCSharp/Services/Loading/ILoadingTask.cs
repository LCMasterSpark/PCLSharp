namespace PCLrmkBYCSharp.Services.Loading;

public interface ILoadingTask
{
    string Name { get; }

    LoadState State { get; }

    double Progress { get; }

    Exception? Exception { get; }

    Task RunAsync(CancellationToken cancellationToken = default);
}
