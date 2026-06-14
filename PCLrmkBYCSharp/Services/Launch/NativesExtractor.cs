using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class NativesExtractor(IAppLoggerService logger) : INativesExtractor
{
    public Task<string> ExtractAsync(MinecraftInstance instance, CancellationToken cancellationToken = default)
    {
        var nativesDirectory = Path.Combine(instance.VersionPath, $"{instance.Name}-natives");
        Directory.CreateDirectory(nativesDirectory);
        var extractedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in LoadVersionDocuments(instance))
        {
            if (!document.Root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var library in libraries.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var excludes = GetExtractExcludes(library);
                foreach (var nativeJar in GetNativeJars(instance.RootPath, library))
                {
                    if (File.Exists(nativeJar))
                    {
                        ExtractJar(nativeJar, nativesDirectory, excludes, extractedFiles);
                    }
                }
            }
        }

        DeleteStaleNativeFiles(nativesDirectory, extractedFiles);
        return Task.FromResult(nativesDirectory);
    }

    private static IReadOnlyList<VersionDocument> LoadVersionDocuments(MinecraftInstance instance)
    {
        var result = new List<VersionDocument>();
        var currentName = instance.Name;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(currentName) && visited.Add(currentName))
        {
            var versionPath = string.Equals(currentName, instance.Name, StringComparison.OrdinalIgnoreCase)
                ? instance.VersionPath
                : Path.Combine(instance.RootPath, "versions", currentName);
            var jsonPath = Path.Combine(versionPath, currentName + ".json");
            if (!File.Exists(jsonPath))
            {
                break;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = document.RootElement.Clone();
            result.Add(new VersionDocument(currentName, versionPath, jsonPath, root));
            currentName = GetString(root, "inheritsFrom");
        }

        return result;
    }

    private static IEnumerable<string> GetNativeJars(string rootPath, JsonElement library)
    {
        if (!library.TryGetProperty("natives", out var natives)
            || !natives.TryGetProperty("windows", out var windowsNative)
            || windowsNative.ValueKind != JsonValueKind.String)
        {
            foreach (var fallbackPath in GetFallbackWindowsNativeJars(rootPath, library))
            {
                yield return fallbackPath;
            }

            yield break;
        }

        var classifierName = windowsNative.GetString()!
            .Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
        if (library.TryGetProperty("downloads", out var downloads)
            && downloads.TryGetProperty("classifiers", out var classifiers)
            && classifiers.ValueKind == JsonValueKind.Object
            && classifiers.TryGetProperty(classifierName, out var classifier)
            && classifier.TryGetProperty("path", out var path)
            && path.ValueKind == JsonValueKind.String)
        {
            yield return Path.Combine(rootPath, "libraries", path.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            yield break;
        }

        var name = GetString(library, "name");
        var relativePath = GetMavenRelativePath(name, classifierName);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            yield return Path.Combine(rootPath, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    private static IEnumerable<string> GetFallbackWindowsNativeJars(string rootPath, JsonElement library)
    {
        if (!library.TryGetProperty("downloads", out var downloads)
            || !downloads.TryGetProperty("classifiers", out var classifiers)
            || classifiers.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var classifier in classifiers.EnumerateObject())
        {
            if (!classifier.Name.Contains("windows", StringComparison.OrdinalIgnoreCase)
                || !classifier.Value.TryGetProperty("path", out var path)
                || path.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            yield return Path.Combine(rootPath, "libraries", path.GetString()!.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    private void ExtractJar(string jarPath, string nativesDirectory, IReadOnlyList<string> excludes, HashSet<string> extractedFiles)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name)
                    || IsExcluded(entry.FullName, excludes)
                    || !entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = GetSafeExtractPath(nativesDirectory, entry);
                if (string.IsNullOrWhiteSpace(target))
                {
                    logger.Warn($"Skip unsafe natives entry: {entry.FullName}");
                    continue;
                }

                extractedFiles.Add(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var targetInfo = new FileInfo(target);
                if (targetInfo.Exists && targetInfo.Length == entry.Length)
                {
                    continue;
                }

                try
                {
                    entry.ExtractToFile(target, overwrite: true);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.Warn($"跳过被占用的 natives 文件：{target}，{ex.Message}");
                }
            }
        }
        catch (InvalidDataException ex)
        {
            logger.Warn($"natives 文件不是有效压缩包，已跳过：{jarPath}，{ex.Message}");
        }
    }

    private void DeleteStaleNativeFiles(string nativesDirectory, HashSet<string> extractedFiles)
    {
        foreach (var file in Directory.EnumerateFiles(nativesDirectory, "*", SearchOption.AllDirectories))
        {
            if (extractedFiles.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Warn($"删除多余 natives 文件时访问被拒绝，已跳过：{file}；{ex.Message}");
                return;
            }
        }
    }

    private static IReadOnlyList<string> GetExtractExcludes(JsonElement library)
    {
        if (!library.TryGetProperty("extract", out var extract)
            || !extract.TryGetProperty("exclude", out var exclude)
            || exclude.ValueKind != JsonValueKind.Array)
        {
            return ["META-INF/"];
        }

        var values = exclude.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        if (!values.Any(value => value.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)))
        {
            values.Add("META-INF/");
        }

        return values;
    }

    private static bool IsExcluded(string entryName, IReadOnlyList<string> excludes)
    {
        var normalized = entryName.Replace('\\', '/');
        return excludes.Any(exclude =>
        {
            var normalizedExclude = exclude.Replace('\\', '/');
            return normalized.StartsWith(normalizedExclude, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string GetSafeExtractPath(string nativesDirectory, ZipArchiveEntry entry)
    {
        var relative = entry.FullName
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(nativesDirectory, relative));
        var root = Path.GetFullPath(nativesDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? target : "";
    }

    private static string GetMavenRelativePath(string name, string classifier)
    {
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            return "";
        }

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        return $"{group}/{artifact}/{version}/{artifact}-{version}-{classifier}.jar";
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private sealed record VersionDocument(string Name, string VersionPath, string JsonPath, JsonElement Root);
}
