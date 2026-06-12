using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchPreRunService
{
    Task PrepareAsync(LaunchRequest request, LoginSession login, CancellationToken cancellationToken = default);
}
