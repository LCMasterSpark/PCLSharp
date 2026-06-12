using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchPatchService
{
    Task<LaunchPatchPrepareResult> PrepareAsync(LaunchProfile profile, CancellationToken cancellationToken = default);
}
