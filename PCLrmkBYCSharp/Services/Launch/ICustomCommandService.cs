using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ICustomCommandService
{
    Task RunAsync(LaunchRequest request, LaunchProfile profile, CancellationToken cancellationToken = default);
}
