using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed partial class LaunchScriptExporter(IAppSettingsService? settings = null) : ILaunchScriptExporter
{
    public Task<string?> ExportAsync(LaunchProfile profile, string targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Task.FromResult<string?>(null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var customCommands = GetCustomCommands(profile);
        var encodingHeader = profile.Java.MajorVersion > 8 ? "chcp 65001>nul" + Environment.NewLine : "";
        var text = $"""
        {encodingHeader}@echo off
        title 启动 - {profile.Instance.Name}
        echo 游戏正在启动，请稍候。
        cd /D "{profile.ProcessStartInfo.WorkingDirectory}"
        {customCommands}
        "{profile.Java.PathJava}" {profile.Arguments}
        echo 游戏已退出。
        pause
        """;
        File.WriteAllText(targetPath, Sanitize(text).Replace("%", "%%", StringComparison.Ordinal), profile.Java.MajorVersion > 8 ? Encoding.UTF8 : Encoding.Default);
        return Task.FromResult<string?>(targetPath);
    }

    private string GetCustomCommands(LaunchProfile profile)
    {
        if (settings is null)
        {
            return "";
        }

        var commands = new List<string>();
        var global = LaunchVariableReplacer.Replace(settings.Get(AppSettingKeys.LaunchAdvanceRun, ""), profile, settings: settings);
        if (!string.IsNullOrWhiteSpace(global))
        {
            commands.Add(global);
        }

        var version = LaunchVariableReplacer.Replace(settings.Get($"Instance.{profile.Instance.Name}.{AppSettingKeys.VersionAdvanceRun}", ""), profile, settings: settings);
        if (!string.IsNullOrWhiteSpace(version))
        {
            commands.Add(version);
        }

        return string.Join(Environment.NewLine, commands);
    }

    private static string Sanitize(string value)
    {
        return AccessTokenRegex().Replace(value, "$1***");
    }

    [GeneratedRegex(@"(--(?:accessToken|access_token|auth_access_token)\s+)(""[^""]*""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();
}
