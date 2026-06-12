using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface INeoForgeLoaderInstallService
{
    Task<LoaderInstallPlan> CreateInstallPlanAsync(
        string minecraftRootPath,
        string instanceName,
        string instancePath,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken = default);
}
