using System.IO;
using System.Text.Json.Nodes;
using Microsoft.VisualBasic.FileIO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class MinecraftInstanceManagementService : IMinecraftInstanceManagementService
{
    private const string IsStarKey = "IsStar";
    private const string DisplayTypeKey = "DisplayType";
    private const string CustomInfoKey = "CustomInfo";

    public MinecraftInstanceMetadata ReadMetadata(string versionPath)
    {
        var values = ReadSetupIni(versionPath);
        return new MinecraftInstanceMetadata(
            TryReadBool(values, IsStarKey),
            TryReadDisplayType(values, DisplayTypeKey),
            values.GetValueOrDefault(CustomInfoKey, ""));
    }

    public void SetStar(MinecraftInstance instance, bool isStar)
    {
        WriteSetupValue(instance.VersionPath, IsStarKey, isStar ? "True" : "False");
    }

    public void SetDisplayType(MinecraftInstance instance, MinecraftInstanceDisplayType displayType)
    {
        WriteSetupValue(instance.VersionPath, DisplayTypeKey, ((int)displayType).ToString());
    }

    public void SetCustomInfo(MinecraftInstance instance, string customInfo)
    {
        WriteSetupValue(instance.VersionPath, CustomInfoKey, customInfo.Trim());
    }

    public string RenameInstance(MinecraftInstance instance, string newName)
    {
        newName = newName.Trim();
        ValidateNewInstanceName(instance, newName);

        var oldName = instance.Name;
        var oldPath = Path.GetFullPath(instance.VersionPath);
        var versionsRoot = GetVersionsRoot(instance);
        var newPath = Path.GetFullPath(Path.Combine(versionsRoot, newName));
        if (!newPath.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("新版本名生成的路径不在 versions 目录内。");
        }

        if (Directory.Exists(newPath))
        {
            throw new InvalidOperationException("目标版本已存在：" + newName);
        }

        Directory.Move(oldPath, newPath);

        RenameInstanceFiles(newPath, oldName, newName);
        RewriteSetupPaths(newPath, oldPath, newPath);
        RewriteVersionJson(newPath, oldName, newName);
        return newPath;
    }

    public string CloneInstance(MinecraftInstance instance, string newName)
    {
        newName = newName.Trim();
        ValidateNewInstanceName(instance, newName);

        var oldName = instance.Name;
        var oldPath = Path.GetFullPath(instance.VersionPath);
        var versionsRoot = GetVersionsRoot(instance);
        var newPath = Path.GetFullPath(Path.Combine(versionsRoot, newName));
        if (!oldPath.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase) ||
            !newPath.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("版本路径不在当前 Minecraft 文件夹的 versions 目录内。");
        }

        if (!Directory.Exists(oldPath))
        {
            throw new InvalidOperationException("源版本目录不存在：" + oldName);
        }

        if (Directory.Exists(newPath))
        {
            throw new InvalidOperationException("目标版本已存在：" + newName);
        }

        CopyDirectory(oldPath, newPath);
        RenameInstanceFiles(newPath, oldName, newName);
        RewriteSetupPaths(newPath, oldPath, newPath);
        RewriteVersionJson(newPath, oldName, newName);
        return newPath;
    }

    public string ImportInstance(string sourceVersionPath, string targetMinecraftRoot, string? targetName = null)
    {
        if (string.IsNullOrWhiteSpace(sourceVersionPath))
        {
            throw new InvalidOperationException("请选择要导入的版本文件夹。");
        }

        if (string.IsNullOrWhiteSpace(targetMinecraftRoot))
        {
            throw new InvalidOperationException("当前 Minecraft 文件夹无效。");
        }

        var sourcePath = Path.GetFullPath(sourceVersionPath);
        if (!Directory.Exists(sourcePath))
        {
            throw new InvalidOperationException("要导入的版本文件夹不存在：" + sourceVersionPath);
        }

        var sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var newName = string.IsNullOrWhiteSpace(targetName) ? sourceName : targetName.Trim();
        ValidateInstanceName(newName);

        var versionsRoot = Path.GetFullPath(Path.Combine(targetMinecraftRoot, "versions"));
        if (!versionsRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            versionsRoot += Path.DirectorySeparatorChar;
        }

        var newPath = Path.GetFullPath(Path.Combine(versionsRoot, newName));
        if (!newPath.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("导入目标不在当前 Minecraft 文件夹的 versions 目录内。");
        }

        if (sourcePath.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("该版本已经位于当前 Minecraft 文件夹中，请直接刷新列表。");
        }

        if (Directory.Exists(newPath))
        {
            throw new InvalidOperationException("目标版本已存在：" + newName);
        }

        var sourceJson = FindVersionJson(sourcePath, sourceName);
        if (sourceJson is null)
        {
            throw new InvalidOperationException("未找到版本 JSON，请选择包含版本 JSON 的单个版本文件夹。");
        }

        Directory.CreateDirectory(versionsRoot);
        CopyDirectory(sourcePath, newPath);
        var importedJson = Path.Combine(newPath, Path.GetFileName(sourceJson));
        var normalizedSourceJson = Path.Combine(newPath, sourceName + ".json");
        if (!string.Equals(importedJson, normalizedSourceJson, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(normalizedSourceJson))
            {
                File.Delete(normalizedSourceJson);
            }

            File.Move(importedJson, normalizedSourceJson);
        }

        if (!string.Equals(sourceName, newName, StringComparison.OrdinalIgnoreCase))
        {
            RenameInstanceFiles(newPath, sourceName, newName);
            RewriteVersionJson(newPath, sourceName, newName);
        }
        else
        {
            RewriteVersionJson(newPath, sourceName, newName);
        }

        if (!File.Exists(Path.Combine(newPath, newName + ".json")))
        {
            File.Move(importedJson, Path.Combine(newPath, newName + ".json"));
        }

        RewriteSetupPaths(newPath, sourcePath, newPath);
        return newPath;
    }

    public void DeleteInstance(MinecraftInstance instance, bool permanent = false)
    {
        var versionPath = Path.GetFullPath(instance.VersionPath);
        var versionsRoot = GetVersionsRoot(instance);

        if (!versionPath.StartsWith(versionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("版本路径不在当前 Minecraft 文件夹的 versions 目录内，已阻止删除。");
        }

        if (!Directory.Exists(versionPath))
        {
            return;
        }

        if (permanent)
        {
            Directory.Delete(versionPath, recursive: true);
            return;
        }

        FileSystem.DeleteDirectory(
            versionPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }

    private static string GetSetupPath(string versionPath)
    {
        return Path.Combine(versionPath, "PCL", "Setup.ini");
    }

    private static string GetVersionsRoot(MinecraftInstance instance)
    {
        var versionsRoot = Path.GetFullPath(Path.Combine(instance.RootPath, "versions"));
        if (!versionsRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            versionsRoot += Path.DirectorySeparatorChar;
        }

        return versionsRoot;
    }

    private static void ValidateNewInstanceName(MinecraftInstance instance, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("版本名不能为空。");
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("版本名包含非法文件名字符。");
        }

        if (string.Equals(instance.Name, newName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("新版本名与原版本名相同。");
        }
    }

    private static void ValidateInstanceName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("版本名不能为空。");
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("版本名包含非法文件名字符。");
        }
    }

    private static string? FindVersionJson(string versionPath, string sourceName)
    {
        var preferred = Path.Combine(versionPath, sourceName + ".json");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var jsonFiles = Directory.EnumerateFiles(versionPath, "*.json", System.IO.SearchOption.TopDirectoryOnly).ToList();
        return jsonFiles.Count == 1 ? jsonFiles[0] : null;
    }

    private static void RenameInstanceFiles(string versionPath, string oldName, string newName)
    {
        var oldNatives = Path.Combine(versionPath, oldName + "-natives");
        if (Directory.Exists(oldNatives))
        {
            Directory.Move(oldNatives, Path.Combine(versionPath, newName + "-natives"));
        }

        var oldJar = Path.Combine(versionPath, oldName + ".jar");
        if (File.Exists(oldJar))
        {
            File.Move(oldJar, Path.Combine(versionPath, newName + ".jar"));
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", System.IO.SearchOption.AllDirectories))
        {
            var target = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory));
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", System.IO.SearchOption.AllDirectories))
        {
            var target = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void RewriteSetupPaths(string newPath, string oldPath, string targetPath)
    {
        var setupPath = GetSetupPath(newPath);
        if (!File.Exists(setupPath))
        {
            return;
        }

        var content = File.ReadAllText(setupPath).Replace(oldPath, targetPath, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(setupPath, content);
    }

    private static void RewriteVersionJson(string newPath, string oldName, string newName)
    {
        var oldJson = Path.Combine(newPath, oldName + ".json");
        var newJson = Path.Combine(newPath, newName + ".json");
        if (!File.Exists(oldJson))
        {
            return;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(oldJson)) as JsonObject;
            if (node is not null)
            {
                node["id"] = newName;
                File.WriteAllText(newJson, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(oldJson);
                }
            }
        }
        catch
        {
            if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(oldJson, newJson);
            }
        }
    }

    private static Dictionary<string, string> ReadSetupIni(string versionPath)
    {
        var setupPath = GetSetupPath(versionPath);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(setupPath))
        {
            return values;
        }

        foreach (var line in File.ReadAllLines(setupPath))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static void WriteSetupValue(string versionPath, string key, string value)
    {
        var setupPath = GetSetupPath(versionPath);
        var values = ReadSetupIni(versionPath);
        values[key] = value;

        Directory.CreateDirectory(Path.GetDirectoryName(setupPath)!);
        File.WriteAllLines(
            setupPath,
            values.Select(pair => pair.Key + ":" + pair.Value));
    }

    private static bool TryReadBool(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;
    }

    private static MinecraftInstanceDisplayType TryReadDisplayType(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return MinecraftInstanceDisplayType.Auto;
        }

        if (int.TryParse(value, out var numeric) && Enum.IsDefined(typeof(MinecraftInstanceDisplayType), numeric))
        {
            return (MinecraftInstanceDisplayType)numeric;
        }

        return Enum.TryParse<MinecraftInstanceDisplayType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : MinecraftInstanceDisplayType.Auto;
    }
}
