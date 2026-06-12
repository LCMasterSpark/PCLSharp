using System.IO;

namespace PCLrmkBYCSharp.Services;

public sealed class MinecraftSelectionService : IMinecraftSelectionService
{
    private const string VersionKey = "Version";
    private const string InstanceCacheKey = "InstanceCache";

    public string ReadSelectedInstanceName(string minecraftRootPath)
    {
        var path = GetPclIniPath(minecraftRootPath);
        if (!File.Exists(path))
        {
            return "";
        }

        return ReadValues(path).GetValueOrDefault(VersionKey, "");
    }

    public void WriteSelectedInstanceName(string minecraftRootPath, string instanceName)
    {
        if (string.IsNullOrWhiteSpace(minecraftRootPath))
        {
            return;
        }

        var path = GetPclIniPath(minecraftRootPath);
        var values = ReadValues(path);
        values[VersionKey] = instanceName.Trim();
        WriteValues(path, values);
    }

    public void ClearInstanceCache(string minecraftRootPath)
    {
        if (string.IsNullOrWhiteSpace(minecraftRootPath))
        {
            return;
        }

        var path = GetPclIniPath(minecraftRootPath);
        var values = ReadValues(path);
        values[InstanceCacheKey] = "";
        WriteValues(path, values);
    }

    private static Dictionary<string, string> ReadValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static void WriteValues(string path, Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, values.Select(pair => pair.Key + ":" + pair.Value));
    }

    private static string GetPclIniPath(string minecraftRootPath)
    {
        return Path.Combine(Path.GetFullPath(minecraftRootPath), "PCL.ini");
    }
}
