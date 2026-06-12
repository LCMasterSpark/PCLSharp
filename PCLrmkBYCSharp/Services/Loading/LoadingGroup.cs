namespace PCLrmkBYCSharp.Services.Loading;

public sealed class LoadingGroup(string name, IEnumerable<ILoadingTask> tasks)
{
    private readonly IReadOnlyList<ILoadingTask> _tasks = tasks.ToList();

    public string Name { get; } = name;

    public IReadOnlyList<ILoadingTask> Tasks => _tasks;

    public LoadState State { get; private set; } = LoadState.Waiting;

    public double Progress => _tasks.Count == 0 ? 1 : _tasks.Average(task => task.Progress);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        State = LoadState.Running;

        try
        {
            foreach (var task in _tasks)
            {
                await task.RunAsync(cancellationToken);
            }

            State = LoadState.Succeeded;
        }
        catch (OperationCanceledException)
        {
            State = LoadState.Canceled;
            throw;
        }
        catch
        {
            State = LoadState.Failed;
            throw;
        }
    }
}
