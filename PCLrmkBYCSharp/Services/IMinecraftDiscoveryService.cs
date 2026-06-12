using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IMinecraftDiscoveryService
{
    string GetDefaultMinecraftRoot();

    Task<IReadOnlyList<MinecraftInstance>> ScanAsync(string? rootPath, CancellationToken cancellationToken = default);

    MinecraftInstance InspectInstance(string rootPath, string versionPath, IReadOnlySet<string> availableInstances);
}
