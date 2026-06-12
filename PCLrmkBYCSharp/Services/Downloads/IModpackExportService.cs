using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface IModpackExportService
{
    Task<ModpackExportResult> ExportModrinthAsync(
        MinecraftInstance instance,
        string gameDirectory,
        string targetPath,
        string packName,
        string packVersion,
        ModpackExportOptions? options = null,
        CancellationToken cancellationToken = default);
}
