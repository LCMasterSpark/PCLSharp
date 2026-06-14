using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class ModpackExportService(IAppLoggerService logger) : IModpackExportService
{
    public Task<ModpackExportResult> ExportModrinthAsync(
        MinecraftInstance instance,
        string gameDirectory,
        string targetPath,
        string packName,
        string packVersion,
        ModpackExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("导出路径不能为空。", nameof(targetPath));
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var warnings = new List<string>();
        var tempPath = targetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var isCurseForgeZip = string.Equals(Path.GetExtension(targetPath), ".zip", StringComparison.OrdinalIgnoreCase);
        int overrideCount;
        using (var stream = File.Create(tempPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            if (isCurseForgeZip)
            {
                WriteCurseForgeManifest(archive, instance, packName, packVersion);
            }
            else
            {
                WriteModrinthIndex(archive, instance, packName, packVersion);
            }

            overrideCount = AddOverrides(archive, gameDirectory, options ?? new ModpackExportOptions(), warnings, cancellationToken);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
        logger.Info($"已导出{(isCurseForgeZip ? " CurseForge" : " Modrinth")} 整合包：{targetPath}");
        return Task.FromResult(new ModpackExportResult(targetPath, overrideCount, warnings));
    }

    private static void WriteModrinthIndex(ZipArchive archive, MinecraftInstance instance, string packName, string packVersion)
    {
        var dependencies = new JsonObject
        {
            ["minecraft"] = string.IsNullOrWhiteSpace(instance.Version.VanillaVersion)
                ? instance.Version.Id
                : instance.Version.VanillaVersion
        };
        AddDetectedLoaderDependencies(dependencies, instance);
        var index = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["name"] = string.IsNullOrWhiteSpace(packName) ? instance.Name : packName.Trim(),
            ["versionId"] = string.IsNullOrWhiteSpace(packVersion) ? "1.0.0" : packVersion.Trim(),
            ["summary"] = "Exported by PCL Sharp",
            ["files"] = new JsonArray(),
            ["dependencies"] = dependencies
        };

        WriteTextEntry(archive, "modrinth.index.json", index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteCurseForgeManifest(ZipArchive archive, MinecraftInstance instance, string packName, string packVersion)
    {
        var minecraftVersion = string.IsNullOrWhiteSpace(instance.Version.VanillaVersion)
            ? instance.Version.Id
            : instance.Version.VanillaVersion;
        var modLoaders = new JsonArray();
        foreach (var loaderId in ReadCurseForgeLoaderIds(instance, minecraftVersion))
        {
            modLoaders.Add(new JsonObject
            {
                ["id"] = loaderId,
                ["primary"] = modLoaders.Count == 0
            });
        }

        var manifest = new JsonObject
        {
            ["minecraft"] = new JsonObject
            {
                ["version"] = minecraftVersion,
                ["modLoaders"] = modLoaders
            },
            ["manifestType"] = "minecraftModpack",
            ["manifestVersion"] = 1,
            ["name"] = string.IsNullOrWhiteSpace(packName) ? instance.Name : packName.Trim(),
            ["version"] = string.IsNullOrWhiteSpace(packVersion) ? "1.0.0" : packVersion.Trim(),
            ["author"] = "PCL Sharp",
            ["files"] = new JsonArray(),
            ["overrides"] = "overrides"
        };

        WriteTextEntry(archive, "manifest.json", manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IReadOnlyList<string> ReadCurseForgeLoaderIds(MinecraftInstance instance, string minecraftVersion)
    {
        var loaderIds = new List<string>();
        AddCurseForgeLoaderId(loaderIds, "forge", instance.Version.ForgeVersion);
        AddCurseForgeLoaderId(loaderIds, "fabric", instance.Version.FabricVersion);
        AddCurseForgeLoaderId(loaderIds, "neoforge", instance.Version.NeoForgeVersion);
        if (string.IsNullOrWhiteSpace(instance.JsonPath) || !File.Exists(instance.JsonPath))
        {
            return loaderIds;
        }

        try
        {
            using var stream = File.OpenRead(instance.JsonPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Array)
            {
                return loaderIds;
            }

            foreach (var library in libraries.EnumerateArray())
            {
                if (library.ValueKind != JsonValueKind.Object ||
                    !library.TryGetProperty("name", out var nameProperty) ||
                    nameProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var coordinate = nameProperty.GetString();
                var loaderId = TryCreateCurseForgeLoaderId(coordinate, minecraftVersion);
                AddCurseForgeLoaderId(loaderIds, loaderId);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return loaderIds;
    }

    private static void AddCurseForgeLoaderId(List<string> loaderIds, string loaderName, string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            AddCurseForgeLoaderId(loaderIds, loaderName + "-" + version);
        }
    }

    private static void AddCurseForgeLoaderId(List<string> loaderIds, string? loaderId)
    {
        if (!string.IsNullOrWhiteSpace(loaderId) &&
            !loaderIds.Contains(loaderId, StringComparer.OrdinalIgnoreCase))
        {
            loaderIds.Add(loaderId);
        }
    }

    private static string TryCreateCurseForgeLoaderId(string? coordinate, string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
        {
            return "";
        }

        var parts = coordinate.Split(':');
        if (parts.Length < 3)
        {
            return "";
        }

        return (parts[0], parts[1]) switch
        {
            ("net.fabricmc", "fabric-loader") => "fabric-" + parts[2],
            ("org.quiltmc", "quilt-loader") => "quilt-" + parts[2],
            ("net.minecraftforge", "forge") => "forge-" + NormalizeForgeVersion(parts[2], minecraftVersion),
            ("net.neoforged", "neoforge") => "neoforge-" + parts[2],
            _ => ""
        };
    }

    private static void AddDetectedLoaderDependencies(JsonObject dependencies, MinecraftInstance instance)
    {
        AddMetadataLoaderDependency(dependencies, "fabric-loader", instance.Version.FabricVersion);
        AddMetadataLoaderDependency(dependencies, "forge", instance.Version.ForgeVersion);
        AddMetadataLoaderDependency(dependencies, "neoforge", instance.Version.NeoForgeVersion);
        if (string.IsNullOrWhiteSpace(instance.JsonPath) || !File.Exists(instance.JsonPath))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(instance.JsonPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var minecraftVersion = dependencies["minecraft"]?.GetValue<string>() ?? instance.Version.Id;
            foreach (var library in libraries.EnumerateArray())
            {
                if (library.ValueKind != JsonValueKind.Object ||
                    !library.TryGetProperty("name", out var nameProperty) ||
                    nameProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var coordinate = nameProperty.GetString();
                if (string.IsNullOrWhiteSpace(coordinate))
                {
                    continue;
                }

                AddLoaderDependency(dependencies, "fabric-loader", coordinate, "net.fabricmc", "fabric-loader", minecraftVersion);
                AddLoaderDependency(dependencies, "quilt-loader", coordinate, "org.quiltmc", "quilt-loader", minecraftVersion);
                AddLoaderDependency(dependencies, "forge", coordinate, "net.minecraftforge", "forge", minecraftVersion);
                AddLoaderDependency(dependencies, "neoforge", coordinate, "net.neoforged", "neoforge", minecraftVersion);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AddMetadataLoaderDependency(JsonObject dependencies, string key, string version)
    {
        if (!dependencies.ContainsKey(key) && !string.IsNullOrWhiteSpace(version))
        {
            dependencies[key] = version;
        }
    }

    private static void AddLoaderDependency(
        JsonObject dependencies,
        string key,
        string coordinate,
        string expectedGroup,
        string expectedArtifact,
        string minecraftVersion)
    {
        if (dependencies.ContainsKey(key))
        {
            return;
        }

        var parts = coordinate.Split(':');
        if (parts.Length < 3 ||
            !string.Equals(parts[0], expectedGroup, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[1], expectedArtifact, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var version = parts[2];
        if (key == "forge")
        {
            version = NormalizeForgeVersion(version, minecraftVersion);
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            dependencies[key] = version;
        }
    }

    private static string NormalizeForgeVersion(string version, string minecraftVersion)
    {
        var prefix = minecraftVersion + "-";
        return version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? version[prefix.Length..]
            : version;
    }

    private static int AddOverrides(ZipArchive archive, string gameDirectory, ModpackExportOptions options, List<string> warnings, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(root))
        {
            warnings.Add("游戏目录不存在，未写入 overrides。");
            return 0;
        }

        var count = 0;
        foreach (var relative in GetOverridePaths(options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(Path.Combine(root, relative));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                AddFile(archive, fullPath, "overrides/" + NormalizeEntryName(relative));
                count++;
            }
            else if (Directory.Exists(fullPath))
            {
                count += AddDirectory(archive, fullPath, root, warnings, cancellationToken);
            }
        }

        return count;
    }

    private static IEnumerable<string> GetOverridePaths(ModpackExportOptions options)
    {
        if (options.IncludeConfig)
        {
            yield return "config";
            yield return "defaultconfigs";
        }

        if (options.IncludeMods)
        {
            yield return "mods";
        }

        if (options.IncludeResourcePacks)
        {
            yield return "resourcepacks";
            yield return "texturepacks";
        }

        if (options.IncludeShaderPacks)
        {
            yield return "shaderpacks";
        }

        if (options.IncludeSaves)
        {
            yield return "saves";
        }

        if (options.IncludeScreenshots)
        {
            yield return "screenshots";
        }

        if (options.IncludeOptions)
        {
            yield return "options.txt";
            yield return "optionsof.txt";
            yield return "optionsshaders.txt";
            yield return "servers.dat";
        }

        if (options.IncludeExtraData)
        {
            yield return "datapacks";
            yield return "kubejs";
            yield return "openloader";
        }
    }

    private static int AddDirectory(ZipArchive archive, string directory, string root, List<string> warnings, CancellationToken cancellationToken)
    {
        var count = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            warnings.Add($"跳过无法访问的目录：{directory}（{ex.Message}）");
            return 0;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                continue;
            }

            AddFile(archive, file, "overrides/" + NormalizeEntryName(relative));
            count++;
        }

        return count;
    }

    private static void AddFile(ZipArchive archive, string filePath, string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = File.OpenRead(filePath);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string NormalizeEntryName(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }
}
