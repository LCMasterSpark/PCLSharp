using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IJavaDiscoveryService
{
    Task<IReadOnlyList<JavaEntry>> DiscoverAsync(string minecraftRootPath, string? instancePath, CancellationToken cancellationToken = default);

    Task<JavaEntry?> InspectJavaAsync(string javaPath, bool isUserImport = false, CancellationToken cancellationToken = default);
}
