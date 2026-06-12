using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchScriptExporter
{
    Task<string?> ExportAsync(LaunchProfile profile, string targetPath, CancellationToken cancellationToken = default);
}
