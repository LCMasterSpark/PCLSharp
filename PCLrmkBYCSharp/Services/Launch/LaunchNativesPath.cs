using System.IO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

internal static class LaunchNativesPath
{
    public static string GetDirectory(MinecraftInstance instance, bool ensureCreated = false)
    {
        var directory = Path.Combine(instance.VersionPath, $"{instance.Name}-natives");
        if (ensureCreated)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }
}
