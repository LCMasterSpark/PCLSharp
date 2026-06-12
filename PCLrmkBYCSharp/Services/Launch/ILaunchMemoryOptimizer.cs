namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchMemoryOptimizer
{
    Task<LaunchMemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken = default);
}

public sealed record LaunchMemoryOptimizeResult(int ProcessCount);
