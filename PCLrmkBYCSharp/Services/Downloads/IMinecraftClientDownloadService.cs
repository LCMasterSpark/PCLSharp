using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface IMinecraftClientDownloadService
{
    Task<IReadOnlyList<MinecraftRemoteVersion>> GetVersionManifestAsync(CancellationToken cancellationToken = default);

    Task<MinecraftClientInstallPlan> CreateInstallPlanAsync(string minecraftRootPath, string versionId, string instanceName, CancellationToken cancellationToken = default);
}
