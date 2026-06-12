using System.IO;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public static class LaunchVariableReplacer
{
    public static string Replace(
        string? text,
        LaunchProfile profile,
        DateTime? now = null,
        bool replaceTime = true,
        IAppSettingsService? settings = null)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{', StringComparison.Ordinal))
        {
            return text ?? "";
        }

        var current = now ?? DateTime.Now;
        var instance = profile.Instance;
        var versionName = string.IsNullOrWhiteSpace(instance.Version.VanillaVersion)
            || string.Equals(instance.Version.VanillaVersion, "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.Version.VanillaVersion, "old", StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.Version.VanillaVersion, "pending", StringComparison.OrdinalIgnoreCase)
                ? instance.Name
                : instance.Version.VanillaVersion;

        var replaced = text
            .Replace("{version_indie}", profile.ProcessStartInfo.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{verindie}", profile.ProcessStartInfo.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{path}", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            .Replace("{java}", profile.Java.PathFolder, StringComparison.OrdinalIgnoreCase)
            .Replace("{minecraft}", instance.RootPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{version_path}", instance.VersionPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{verpath}", instance.VersionPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", instance.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{version}", versionName, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", profile.Login.UserName, StringComparison.OrdinalIgnoreCase)
            .Replace("{uuid}", profile.Login.Uuid.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{login}", GetLoginDisplayName(profile.Login.Type), StringComparison.OrdinalIgnoreCase);
        return replaceTime
            ? replaced
                .Replace("{date}", current.ToString("yyyy/M/d"), StringComparison.OrdinalIgnoreCase)
                .Replace("{time}", current.ToString("HH:mm:ss"), StringComparison.OrdinalIgnoreCase)
                .ReplaceSetupVariables(profile, settings)
            : replaced.ReplaceSetupVariables(profile, settings);
    }

    private static string ReplaceSetupVariables(this string text, LaunchProfile profile, IAppSettingsService? settings)
    {
        if (settings is null || !text.Contains("{setup:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return Regex.Replace(
            text,
            @"\{setup:([a-zA-Z0-9]+)\}",
            match => GetSettingValue(settings, profile.Instance.Name, match.Groups[1].Value),
            RegexOptions.IgnoreCase);
    }

    private static string GetSettingValue(IAppSettingsService settings, string instanceName, string key)
    {
        var instanceKey = $"Instance.{instanceName}.{key}";
        return settings.HasSaved(instanceKey)
            ? settings.Get(instanceKey, "")
            : settings.Get(key, "");
    }

    private static string GetLoginDisplayName(LoginType type)
    {
        return type switch
        {
            LoginType.Ms or LoginType.Microsoft => "正版",
            LoginType.Nide => "统一通行证",
            LoginType.Auth => "Authlib-Injector",
            _ => "离线"
        };
    }
}
