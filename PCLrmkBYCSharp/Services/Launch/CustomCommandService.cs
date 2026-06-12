using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class CustomCommandService(
    IAppSettingsService settings,
    IAppLoggerService logger,
    ICustomCommandRunner? runner = null,
    IMinecraftGameDirectoryService? gameDirectories = null) : ICustomCommandService
{
    private readonly ICustomCommandRunner _runner = runner ?? new CustomCommandRunner();

    public async Task RunAsync(LaunchRequest request, LaunchProfile profile, CancellationToken cancellationToken = default)
    {
        if (request.Instance is null)
        {
            return;
        }

        var gameDirectory = ResolveGameDirectory(request, profile.Instance);
        await RunCommandAsync(LaunchVariableReplacer.Replace(settings.Get(AppSettingKeys.LaunchAdvanceRun, ""), profile, settings: settings), gameDirectory, settings.Get(AppSettingKeys.LaunchAdvanceRunWait, true), cancellationToken).ConfigureAwait(false);
        var versionCommand = settings.Get(GetInstanceSettingKey(request.Instance.Name, AppSettingKeys.VersionAdvanceRun), "");
        var waitVersion = settings.Get(GetInstanceSettingKey(request.Instance.Name, AppSettingKeys.VersionAdvanceRunWait), settings.Get(AppSettingKeys.VersionAdvanceRunWait, true));
        await RunCommandAsync(LaunchVariableReplacer.Replace(versionCommand, profile, settings: settings), gameDirectory, waitVersion, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunCommandAsync(string command, string workingDirectory, bool wait, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        logger.Info("正在执行自定义命令：" + Sanitize(command));
        await _runner.RunAsync(command, workingDirectory, wait, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveGameDirectory(LaunchRequest request, MinecraftInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(request.GameDirectory))
        {
            return System.IO.Path.GetFullPath(request.GameDirectory);
        }

        return gameDirectories?.Resolve(request).Path ?? instance.VersionPath;
    }

    private static string Sanitize(string value)
    {
        return value.Replace("accessToken", "accessToken***", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInstanceSettingKey(string instanceName, string key)
    {
        return $"Instance.{instanceName}.{key}";
    }
}
