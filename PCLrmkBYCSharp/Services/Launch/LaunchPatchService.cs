using System.IO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchPatchService(IAppLoggerService logger, string? sourceDirectory = null, string? targetDirectory = null) : ILaunchPatchService
{
    private readonly string _sourceDirectory = sourceDirectory ?? Path.Combine(AppContext.BaseDirectory, "Resources", "Patches");
    private readonly string? _targetDirectory = targetDirectory;

    public async Task<LaunchPatchPrepareResult> PrepareAsync(LaunchProfile profile, CancellationToken cancellationToken = default)
    {
        var required = GetRequiredPatches(profile.Arguments);
        if (required.Count == 0)
        {
            return LaunchPatchPrepareResult.Ok([]);
        }

        var patchDirectory = string.IsNullOrWhiteSpace(_targetDirectory)
            ? profile.Instance.VersionPath
            : _targetDirectory;
        Directory.CreateDirectory(patchDirectory);
        var prepared = new List<string>();
        var missing = new List<string>();
        foreach (var patch in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(_sourceDirectory, patch.SourceFileName);
            var target = Path.Combine(patchDirectory, patch.TargetFileName);
            if (!File.Exists(source))
            {
                if (File.Exists(target))
                {
                    prepared.Add(target);
                    continue;
                }

                missing.Add(source);
                continue;
            }

            await using var sourceStream = File.OpenRead(source);
            await using var targetStream = File.Create(target);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            prepared.Add(target);
            logger.Info($"已准备启动补丁文件：{target}");
        }

        return missing.Count == 0
            ? LaunchPatchPrepareResult.Ok(prepared)
            : LaunchPatchPrepareResult.Failed(missing);
    }

    private static IReadOnlyList<PatchFile> GetRequiredPatches(string arguments)
    {
        var required = new List<PatchFile>();
        if (arguments.Contains("JavaWrapper.jar", StringComparison.OrdinalIgnoreCase))
        {
            required.Add(new PatchFile("java-wrapper.jar", "JavaWrapper.jar"));
        }

        if (arguments.Contains("LUA.jar", StringComparison.OrdinalIgnoreCase))
        {
            required.Add(new PatchFile("lwjgl-unsafe-agent.jar", "LUA.jar"));
        }

        return required;
    }

    private sealed record PatchFile(string SourceFileName, string TargetFileName);
}
