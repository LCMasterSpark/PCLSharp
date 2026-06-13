using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed partial class JavaSelectorService : IJavaSelectorService
{
    public JavaRequirement GetRequirement(MinecraftInstance instance)
    {
        var min = new Version(0, 0, 0, 0);
        var max = new Version(999, 999, 999, 999);
        var minecraftVersion = ParseMinecraftVersion(instance.Version.VanillaVersion);
        var releaseTime = instance.Version.ReleaseTime?.DateTime;

        if (minecraftVersion >= new Version(20, 0, 5) || releaseTime >= new DateTime(2024, 4, 2))
        {
            min = new Version(1, 21, 0, 0);
            max = new Version(1, 21, 999, 999);
        }
        else if (minecraftVersion.Major >= 18 || releaseTime >= new DateTime(2021, 11, 16))
        {
            min = new Version(1, 17, 0, 0);
            max = new Version(1, 17, 999, 999);
        }
        else if (minecraftVersion.Major >= 17 || releaseTime >= new DateTime(2021, 5, 11))
        {
            min = new Version(1, 16, 0, 0);
            max = new Version(1, 16, 999, 999);
        }
        else if (releaseTime?.Year >= 2017 || minecraftVersion.Major >= 12)
        {
            min = new Version(1, 8, 0, 0);
            max = new Version(1, 8, 999, 999);
        }
        else if (releaseTime is not null && releaseTime <= new DateTime(2013, 5, 1) && releaseTime.Value.Year >= 2001)
        {
            max = new Version(1, 8, 999, 999);
        }

        if (instance.Version.HasOptiFine && minecraftVersion.Major is >= 8 and < 12)
        {
            min = Max(min, new Version(1, 8, 0, 0));
            max = Min(max, new Version(1, 8, 999, 999));
        }

        if (instance.Version.HasForge && minecraftVersion.Major <= 12)
        {
            max = Min(max, new Version(1, 8, 999, 999));
        }

        return new JavaRequirement(min, max);
    }

    public JavaEntry? SelectBest(MinecraftInstance instance, IEnumerable<JavaEntry> candidates, string? preferredJavaPath)
    {
        var requirement = GetRequirement(instance);
        var available = candidates.Where(requirement.Allows).ToList();
        var preferredPath = JavaEntry.ResolveSettingJavaPath(preferredJavaPath);
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var preferred = available.FirstOrDefault(entry => string.Equals(entry.PathJava, preferredPath, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return available
            .OrderByDescending(entry => entry.Is64Bit)
            .ThenBy(entry => entry.Version.Major == requirement.MinVersion.Major && entry.Version.Minor == requirement.MinVersion.Minor ? 0 : 1)
            .ThenBy(entry => Math.Abs(entry.MajorVersion - requirement.MinVersion.Minor))
            .ThenByDescending(entry => entry.Version)
            .FirstOrDefault();
    }

    internal static Version ParseMinecraftVersion(string version)
    {
        var match = MinecraftVersionRegex().Match(version);
        if (!match.Success)
        {
            return new Version(0, 0, 0);
        }

        var parts = match.Value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return new Version(int.Parse(parts[0]), 0, 0);
        }

        if (parts[0] == "1")
        {
            return new Version(
                int.Parse(parts.ElementAtOrDefault(1) ?? "0"),
                0,
                int.Parse(parts.ElementAtOrDefault(2) ?? "0"));
        }

        return new Version(
            int.Parse(parts[0]),
            int.Parse(parts.ElementAtOrDefault(1) ?? "0"),
            int.Parse(parts.ElementAtOrDefault(2) ?? "0"));
    }

    private static Version Max(Version left, Version right) => left >= right ? left : right;

    private static Version Min(Version left, Version right) => left <= right ? left : right;

    [GeneratedRegex(@"[0-9]+(\.[0-9]+){0,2}")]
    private static partial Regex MinecraftVersionRegex();
}
