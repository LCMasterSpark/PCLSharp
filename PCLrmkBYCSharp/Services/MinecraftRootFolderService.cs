using System.IO;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class MinecraftRootFolderService(IAppSettingsService settings) : IMinecraftRootFolderService
{
    public IReadOnlyList<MinecraftRootFolder> LoadFolders(string defaultRootPath, string selectedRootPath)
    {
        var folders = new List<MinecraftRootFolder>();
        AddOrRename(folders, CreateFolder("官方启动器文件夹", defaultRootPath, MinecraftRootFolderType.Vanilla));

        foreach (var entry in ParseStoredFolders(settings.Get(AppSettingKeys.LaunchFolders, "")))
        {
            AddOrRename(folders, entry);
        }

        if (!string.IsNullOrWhiteSpace(selectedRootPath)
            && !folders.Any(folder => PathsEqual(folder.Path, selectedRootPath)))
        {
            AddOrRename(folders, CreateFolder(CreateDisplayName(selectedRootPath), selectedRootPath, MinecraftRootFolderType.Custom));
        }

        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
            .OrderBy(folder => folder.Type == MinecraftRootFolderType.Vanilla ? 0 : 1)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => folder with
            {
                IsCurrent = PathsEqual(folder.Path, selectedRootPath),
                VersionCount = CountVersions(folder.Path)
            })
            .ToArray();
    }

    public MinecraftRootFolder AddFolder(string folderPath, string? displayName = null)
    {
        var normalizedPath = ResolveMinecraftRoot(folderPath);
        ValidatePath(normalizedPath);
        Directory.CreateDirectory(Path.Combine(normalizedPath, "versions"));

        var name = SanitizeName(string.IsNullOrWhiteSpace(displayName) ? CreateDisplayName(normalizedPath) : displayName);
        var folders = ParseStoredFolders(settings.Get(AppSettingKeys.LaunchFolders, "")).ToList();
        var updated = false;
        for (var i = 0; i < folders.Count; i++)
        {
            if (PathsEqual(folders[i].Path, normalizedPath))
            {
                folders[i] = folders[i] with { Name = name, Type = MinecraftRootFolderType.Custom };
                updated = true;
                break;
            }
        }

        if (!updated)
        {
            folders.Add(new MinecraftRootFolder(name, normalizedPath, MinecraftRootFolderType.Custom));
        }

        SaveStoredFolders(folders);
        settings.Set(AppSettingKeys.MinecraftRootPath, normalizedPath);
        settings.Set(AppSettingKeys.LaunchFolderSelect, normalizedPath);
        return new MinecraftRootFolder(name, normalizedPath, MinecraftRootFolderType.Custom);
    }

    public void RemoveFolder(string folderPath)
    {
        var folders = ParseStoredFolders(settings.Get(AppSettingKeys.LaunchFolders, ""))
            .Where(folder => !PathsEqual(folder.Path, folderPath))
            .ToList();
        SaveStoredFolders(folders);
    }

    public MinecraftRootFolder RenameFolder(string folderPath, string displayName)
    {
        var normalizedPath = NormalizePath(folderPath);
        var name = SanitizeName(displayName);
        var folders = ParseStoredFolders(settings.Get(AppSettingKeys.LaunchFolders, "")).ToList();
        var updated = false;
        for (var i = 0; i < folders.Count; i++)
        {
            if (PathsEqual(folders[i].Path, normalizedPath))
            {
                folders[i] = folders[i] with { Name = name };
                updated = true;
                break;
            }
        }

        if (!updated)
        {
            folders.Add(new MinecraftRootFolder(name, normalizedPath, MinecraftRootFolderType.Custom));
        }

        SaveStoredFolders(folders);
        return new MinecraftRootFolder(name, normalizedPath, MinecraftRootFolderType.Custom);
    }

    private static void AddOrRename(List<MinecraftRootFolder> folders, MinecraftRootFolder folder)
    {
        var index = folders.FindIndex(item => PathsEqual(item.Path, folder.Path));
        if (index >= 0)
        {
            var type = folders[index].Type == MinecraftRootFolderType.Vanilla
                ? MinecraftRootFolderType.RenamedVanilla
                : folder.Type;
            folders[index] = folder with { Type = type };
            return;
        }

        folders.Add(folder);
    }

    private static IEnumerable<MinecraftRootFolder> ParseStoredFolders(string value)
    {
        foreach (var item in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = item.IndexOf('>');
            if (separator <= 0 || separator >= item.Length - 1)
            {
                continue;
            }

            var name = SanitizeName(item[..separator]);
            var path = NormalizePath(item[(separator + 1)..]);
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return new MinecraftRootFolder(name, path, MinecraftRootFolderType.Custom);
            }
        }
    }

    private void SaveStoredFolders(IEnumerable<MinecraftRootFolder> folders)
    {
        var stored = folders
            .Where(folder => folder.Type != MinecraftRootFolderType.Vanilla)
            .DistinctBy(folder => NormalizePath(folder.Path), StringComparer.OrdinalIgnoreCase)
            .Select(folder => $"{SanitizeName(folder.Name)}>{NormalizePath(folder.Path)}");
        settings.Set(AppSettingKeys.LaunchFolders, string.Join('|', stored));
    }

    private static MinecraftRootFolder CreateFolder(string name, string path, MinecraftRootFolderType type)
    {
        return new MinecraftRootFolder(SanitizeName(name), NormalizePath(path), type);
    }

    private static string ResolveMinecraftRoot(string folderPath)
    {
        var normalized = NormalizePath(folderPath);
        if (Directory.Exists(Path.Combine(normalized, "versions")) || Path.GetFileName(normalized) == ".minecraft")
        {
            return normalized;
        }

        if (Directory.Exists(normalized))
        {
            foreach (var child in Directory.EnumerateDirectories(normalized))
            {
                if (Directory.Exists(Path.Combine(child, "versions")) || Path.GetFileName(child) == ".minecraft")
                {
                    return NormalizePath(child);
                }
            }
        }

        return normalized;
    }

    private static void ValidatePath(string path)
    {
        if (path.Contains('!') || path.Contains(';'))
        {
            throw new InvalidOperationException("Minecraft 文件夹路径中不能含有感叹号或分号。");
        }
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? ""
            : Path.GetFullPath(path.Trim().Replace('$', Path.DirectorySeparatorChar));
    }

    private static string CreateDisplayName(string path)
    {
        var normalized = NormalizePath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(normalized);
        if (string.Equals(name, ".minecraft", StringComparison.OrdinalIgnoreCase))
        {
            name = Directory.GetParent(normalized)?.Name ?? name;
        }

        return string.IsNullOrWhiteSpace(name) ? "Minecraft 文件夹" : SanitizeName(name);
    }

    private static int CountVersions(string rootPath)
    {
        try
        {
            var versionsPath = Path.Combine(NormalizePath(rootPath), "versions");
            if (!Directory.Exists(versionsPath))
            {
                return 0;
            }

            return Directory.EnumerateDirectories(versionsPath)
                .Count(directory =>
                {
                    var name = Path.GetFileName(directory);
                    return !string.IsNullOrWhiteSpace(name) && File.Exists(Path.Combine(directory, name + ".json"));
                });
        }
        catch
        {
            return 0;
        }
    }

    private static string SanitizeName(string value)
    {
        var name = value.Trim().Replace(">", "", StringComparison.Ordinal).Replace("|", "", StringComparison.Ordinal);
        if (name.Length > 30)
        {
            name = name[..30];
        }

        return string.IsNullOrWhiteSpace(name) ? "Minecraft 文件夹" : name;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }
}
