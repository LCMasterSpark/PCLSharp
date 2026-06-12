using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface IModpackInstallService
{
    Task<ModpackInstallPlan> CreateModrinthInstallPlanAsync(
        string modpackPath,
        string minecraftRootPath,
        string? instanceName = null,
        CancellationToken cancellationToken = default);
}
