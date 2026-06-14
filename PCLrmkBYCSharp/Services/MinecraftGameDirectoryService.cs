using System.IO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class MinecraftGameDirectoryService(IAppSettingsService settings) : IMinecraftGameDirectoryService
{
    public MinecraftGameDirectory Resolve(MinecraftInstance instance)
    {
        var instanceKey = GetInstanceSettingKey(instance.Name, AppSettingKeys.VersionArgumentIndieV2);
        var isolated = settings.HasSaved(instanceKey)
            ? settings.Get(instanceKey, false)
            : ShouldUseVersionIsolation(instance);
        return new MinecraftGameDirectory(GetPath(instance, isolated), isolated);
    }

    public MinecraftGameDirectory Resolve(LaunchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.GameDirectory))
        {
            var path = Path.GetFullPath(request.GameDirectory);
            return new MinecraftGameDirectory(path, request.Instance is not null && PathsEqual(path, request.Instance.VersionPath));
        }

        if (request.Instance is null)
        {
            return new MinecraftGameDirectory(Path.GetFullPath(request.MinecraftRootPath), false);
        }

        return Resolve(request.Instance);
    }

    public string GetPath(MinecraftInstance instance, bool isIsolated)
    {
        return Path.GetFullPath(isIsolated ? instance.VersionPath : instance.RootPath);
    }

    private bool ShouldUseVersionIsolation(MinecraftInstance instance)
    {
        var oldKey = GetInstanceSettingKey(instance.Name, AppSettingKeys.VersionArgumentIndie);
        if (settings.HasSaved(oldKey))
        {
            var oldValue = settings.Get(oldKey, -1);
            if (oldValue > 0)
            {
                return oldValue == 1;
            }
        }

        if (TryReadOldPclVersionIsolation(instance.VersionPath, out var oldPclIsolation))
        {
            return oldPclIsolation;
        }

        if (VersionFolderContainsUserData(instance.VersionPath))
        {
            return true;
        }

        var isRelease = string.Equals(instance.Version.Type, "release", StringComparison.OrdinalIgnoreCase)
            && instance.DisplayType != MinecraftInstanceDisplayType.Fool;
        var modable = instance.Version.HasForge
            || instance.Version.HasNeoForge
            || instance.Version.HasFabric
            || instance.Version.HasOptiFine
            || instance.DisplayType == MinecraftInstanceDisplayType.Api;

        return settings.Get(AppSettingKeys.LaunchArgumentIndieV2, 4) switch
        {
            0 => false,
            1 => modable,
            2 => !isRelease,
            3 => !isRelease || modable,
            _ => true
        };
    }

    private static bool TryReadOldPclVersionIsolation(string versionPath, out bool isolated)
    {
        isolated = false;
        var setupPath = Path.Combine(versionPath, "PCL", "Setup.ini");
        if (!File.Exists(setupPath))
        {
            return false;
        }

        foreach (var line in File.ReadLines(setupPath))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!string.Equals(key, AppSettingKeys.VersionArgumentIndieV2, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            return bool.TryParse(value, out isolated);
        }

        return false;
    }

    private static bool VersionFolderContainsUserData(string versionPath)
    {
        var mods = Path.Combine(versionPath, "mods");
        if (Directory.Exists(mods) && Directory.EnumerateFileSystemEntries(mods).Any())
        {
            return true;
        }

        var saves = Path.Combine(versionPath, "saves");
        return Directory.Exists(saves) && Directory.EnumerateDirectories(saves).Any();
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInstanceSettingKey(string instanceName, string key)
    {
        return $"Instance.{instanceName}.{key}";
    }
}
