using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchPipelineService
{
    event EventHandler<IReadOnlyList<LaunchStepState>>? StepsChanged;

    IReadOnlyList<LaunchStepState> Steps { get; }

    Task<LaunchResult> GenerateProfileAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default);
}
