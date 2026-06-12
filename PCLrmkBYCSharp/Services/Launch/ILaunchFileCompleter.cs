using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchFileCompleter
{
    Task<IReadOnlyList<string>> CheckMissingFilesAsync(LaunchRequest request, IReadOnlyList<string> argumentMissingFiles, CancellationToken cancellationToken = default);

    Task<LaunchFileCompletionResult> BuildCompletionPlanAsync(LaunchRequest request, IReadOnlyList<string> argumentMissingFiles, CancellationToken cancellationToken = default);
}
