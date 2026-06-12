using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class LocalModService(IAppLoggerService? logger = null) : ILocalModService
{
    private static readonly string[] EnabledExtensions = [".jar", ".zip", ".litemod"];
    private static readonly string[] DisabledSuffixes = [".disabled", ".old"];

    public Task<IReadOnlyList<LocalModFile>> ScanAsync(string modsDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modsDirectory) || !Directory.Exists(modsDirectory))
        {
            return Task.FromResult<IReadOnlyList<LocalModFile>>([]);
        }

        var mods = new List<LocalModFile>();
        foreach (var path in Directory.EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsModFile(path))
            {
                continue;
            }

            try
            {
                var mod = CreateModFile(path);
                var duplicate = mods.FindIndex(item => string.Equals(item.EnabledFileName, mod.EnabledFileName, StringComparison.OrdinalIgnoreCase));
                if (duplicate >= 0)
                {
                    if (mods[duplicate].IsEnabled)
                    {
                        logger?.Warn("发现重复 Mod 文件，已忽略：" + mod.FileName);
                        continue;
                    }

                    logger?.Warn("发现重复 Mod 文件，已忽略：" + mods[duplicate].FileName);
                    mods.RemoveAt(duplicate);
                }

                mods.Add(mod);
            }
            catch (Exception ex)
            {
                logger?.Warn("读取本地 Mod 失败：" + path + "，" + ex.Message);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalModFile>>(mods
            .OrderBy(mod => mod.EnabledFileName, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    public LocalModFile SetEnabled(LocalModFile mod, bool enabled)
    {
        if (mod.IsEnabled == enabled)
        {
            return mod;
        }

        var targetPath = enabled
            ? Path.Combine(Path.GetDirectoryName(mod.FilePath) ?? "", mod.EnabledFileName)
            : GetDisabledTargetPath(mod.FilePath);
        if (File.Exists(targetPath))
        {
            throw new IOException("目标 Mod 文件已存在：" + targetPath);
        }

        File.Move(mod.FilePath, targetPath);
        return CreateModFile(targetPath);
    }

    public void Delete(LocalModFile mod)
    {
        if (File.Exists(mod.FilePath))
        {
            File.Delete(mod.FilePath);
        }
    }

    public IReadOnlyList<LocalModFile> Install(string modsDirectory, IEnumerable<string> sourceFiles)
    {
        if (string.IsNullOrWhiteSpace(modsDirectory))
        {
            throw new ArgumentException("Mod 目录不能为空", nameof(modsDirectory));
        }

        Directory.CreateDirectory(modsDirectory);
        var installed = new List<LocalModFile>();
        foreach (var sourceFile in sourceFiles.Where(File.Exists))
        {
            if (!IsModFile(sourceFile))
            {
                continue;
            }

            var targetName = GetEnabledFileName(Path.GetFileName(sourceFile));
            if (!Path.HasExtension(targetName))
            {
                targetName += ".jar";
            }

            var targetPath = Path.Combine(modsDirectory, targetName);
            File.Copy(sourceFile, targetPath, overwrite: true);
            installed.Add(CreateModFile(targetPath));
        }

        return installed;
    }

    private static LocalModFile CreateModFile(string path)
    {
        var fileInfo = new FileInfo(path);
        var enabledName = GetEnabledFileName(fileInfo.Name);
        var metadata = TryReadMetadata(path);
        var baseName = Path.GetFileNameWithoutExtension(enabledName);
        var display = string.IsNullOrWhiteSpace(metadata.DisplayName) ? baseName : metadata.DisplayName;
        return new LocalModFile(
            fileInfo.FullName,
            fileInfo.Name,
            enabledName,
            IsEnabledFileName(fileInfo.Name),
            display,
            metadata.Version,
            metadata.Description,
            fileInfo.Length,
            fileInfo.LastWriteTime);
    }

    private static string GetDisabledTargetPath(string path)
    {
        var disabledPath = path + ".disabled";
        return File.Exists(path + ".old") ? path + ".old" : disabledPath;
    }

    private static bool IsModFile(string path)
    {
        var name = Path.GetFileName(path);
        return IsEnabledFileName(name) || DisabledSuffixes.Any(suffix =>
        {
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var enabledName = name[..^suffix.Length];
            return IsEnabledFileName(enabledName);
        });
    }

    private static bool IsEnabledFileName(string fileName)
    {
        return EnabledExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    private static string GetEnabledFileName(string fileName)
    {
        foreach (var suffix in DisabledSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^suffix.Length];
            }
        }

        return fileName;
    }

    private static LocalModMetadata TryReadMetadata(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return ReadFabricLikeMetadata(archive, "fabric.mod.json")
                ?? ReadFabricLikeMetadata(archive, "quilt.mod.json")
                ?? ReadForgeTomlMetadata(archive)
                ?? ReadMcmodInfoMetadata(archive)
                ?? LocalModMetadata.Empty;
        }
        catch
        {
            return LocalModMetadata.Empty;
        }
    }

    private static LocalModMetadata? ReadFabricLikeMetadata(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var name = GetJsonString(root, "name");
        var version = GetJsonString(root, "version");
        var description = GetJsonString(root, "description");
        return new LocalModMetadata(name, version, description);
    }

    private static LocalModMetadata? ReadMcmodInfoMetadata(ZipArchive archive)
    {
        var entry = archive.GetEntry("mcmod.info");
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault()
            : document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new LocalModMetadata(
            GetJsonString(root, "name"),
            GetJsonString(root, "version"),
            GetJsonString(root, "description"));
    }

    private static LocalModMetadata? ReadForgeTomlMetadata(ZipArchive archive)
    {
        var entry = archive.GetEntry("META-INF/mods.toml");
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return new LocalModMetadata(
            ReadTomlString(content, "displayName"),
            NormalizeVersion(ReadTomlString(content, "version")),
            ReadTomlString(content, "description"));
    }

    private static string ReadTomlString(string content, string key)
    {
        var match = Regex.Match(content, @"(?m)^\s*" + Regex.Escape(key) + @"\s*=\s*""(?<value>[^""]*)""");
        return match.Success ? match.Groups["value"].Value.Trim() : "";
    }

    private static string NormalizeVersion(string value)
    {
        return value.Contains("${", StringComparison.Ordinal) ? "" : value;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Object => GetLocalizedString(value),
            _ => ""
        };
    }

    private static string GetLocalizedString(JsonElement value)
    {
        foreach (var key in new[] { "zh_cn", "zh-CN", "zh", "en_us", "en-US", "en" })
        {
            if (value.TryGetProperty(key, out var localized) && localized.ValueKind == JsonValueKind.String)
            {
                return localized.GetString() ?? "";
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString() ?? "";
            }
        }

        return "";
    }

    private sealed record LocalModMetadata(string DisplayName, string Version, string Description)
    {
        public static LocalModMetadata Empty { get; } = new("", "", "");
    }
}
