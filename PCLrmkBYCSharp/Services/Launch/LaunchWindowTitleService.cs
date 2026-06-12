using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchWindowTitleService(IAppSettingsService settings) : ILaunchWindowTitleService
{
    public string ResolveTitle(LaunchProfile profile)
    {
        var instanceTitle = settings.Get(GetInstanceSettingKey(profile.Instance.Name, AppSettingKeys.VersionArgumentTitle), "");
        var template = string.IsNullOrWhiteSpace(instanceTitle)
            ? settings.Get(AppSettingKeys.LaunchArgumentTitle, "")
            : instanceTitle;
        return LaunchVariableReplacer.Replace(template, profile, replaceTime: false, settings: settings).Trim();
    }

    private static string GetInstanceSettingKey(string instanceName, string key)
    {
        return $"Instance.{instanceName}.{key}";
    }
}
