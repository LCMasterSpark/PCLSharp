using System.Globalization;
using System.IO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public static class LaunchNativesPath
{
    public static string GetDirectory(
        MinecraftInstance instance,
        bool ensureCreated = false,
        string? appDataDirectory = null,
        string? programDataDirectory = null,
        Func<string, string>? shortenPath = null,
        Func<bool>? isGbkEncoding = null)
    {
        var directory = Path.Combine(instance.VersionPath, $"{instance.Name}-natives");
        if (ensureCreated)
        {
            Directory.CreateDirectory(directory);
        }

        shortenPath ??= path => PclSharpPathUtils.ToShortPath(path);
        isGbkEncoding ??= IsGbkSystemEncoding;
        var shortened = shortenPath(directory);
        if (!isGbkEncoding() && !IsAsciiOnly(shortened))
        {
            directory = Path.Combine(
                appDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft",
                "bin",
                "natives");
            if (!IsAsciiOnly(directory))
            {
                directory = Path.Combine(
                    programDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PCLSharp",
                    "natives");
            }

            if (ensureCreated)
            {
                Directory.CreateDirectory(directory);
            }
        }

        return directory;
    }

    private static bool IsAsciiOnly(string value)
    {
        return value.All(ch => ch <= 0x7F);
    }

    private static bool IsGbkSystemEncoding()
    {
        try
        {
            return CultureInfo.CurrentCulture.TextInfo.ANSICodePage == 936;
        }
        catch
        {
            return false;
        }
    }
}
