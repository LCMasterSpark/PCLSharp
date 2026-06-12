using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class ForgeLoaderInstallService(
    IDownloadByteClient client,
    IDownloadSourceService sources,
    IAppLoggerService logger) : IForgeLoaderInstallService
{
    public async Task<LoaderInstallPlan> CreateInstallPlanAsync(
        string minecraftRootPath,
        string instanceName,
        string instancePath,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken = default)
    {
        var installerUrl = BuildInstallerUrl(minecraftVersion, loaderVersion);
        logger.Info("Fetching Forge installer: " + installerUrl);
        return await InstallerProfileReader.CreateInstallPlanAsync(
            client,
            sources,
            installerUrl,
            "forge",
            loaderVersion,
            minecraftRootPath,
            instanceName,
            instancePath,
            minecraftVersion,
            cancellationToken).ConfigureAwait(false);
    }

    public static string BuildInstallerUrl(string minecraftVersion, string loaderVersion)
    {
        var version = minecraftVersion.Trim() + "-" + loaderVersion.Trim();
        return "https://maven.minecraftforge.net/net/minecraftforge/forge/"
            + Uri.EscapeDataString(version)
            + "/forge-"
            + Uri.EscapeDataString(version)
            + "-installer.jar";
    }
}

public sealed class NeoForgeLoaderInstallService(
    IDownloadByteClient client,
    IDownloadSourceService sources,
    IAppLoggerService logger) : INeoForgeLoaderInstallService
{
    public async Task<LoaderInstallPlan> CreateInstallPlanAsync(
        string minecraftRootPath,
        string instanceName,
        string instancePath,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken = default)
    {
        var installerUrl = BuildInstallerUrl(loaderVersion);
        logger.Info("Fetching NeoForge installer: " + installerUrl);
        return await InstallerProfileReader.CreateInstallPlanAsync(
            client,
            sources,
            installerUrl,
            "neoforge",
            loaderVersion,
            minecraftRootPath,
            instanceName,
            instancePath,
            minecraftVersion,
            cancellationToken).ConfigureAwait(false);
    }

    public static string BuildInstallerUrl(string loaderVersion)
    {
        var version = loaderVersion.Trim();
        return "https://maven.neoforged.net/releases/net/neoforged/neoforge/"
            + Uri.EscapeDataString(version)
            + "/neoforge-"
            + Uri.EscapeDataString(version)
            + "-installer.jar";
    }
}

internal static class InstallerProfileReader
{
    public static async Task<LoaderInstallPlan> CreateInstallPlanAsync(
        IDownloadByteClient client,
        IDownloadSourceService sources,
        string installerUrl,
        string loaderName,
        string loaderVersion,
        string minecraftRootPath,
        string instanceName,
        string instancePath,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var bytes = await client.GetBytesAsync(installerUrl, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        JsonObject? installProfile = null;
        var installProfileEntry = archive.GetEntry("install_profile.json");
        if (installProfileEntry is not null)
        {
            await using var installProfileStream = installProfileEntry.Open();
            installProfile = await JsonNode.ParseAsync(installProfileStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
        }

        var versionEntry = archive.GetEntry("version.json")
            ?? throw new InvalidDataException("Installer jar does not contain version.json.");
        await using var versionStream = versionEntry.Open();
        var profile = await JsonNode.ParseAsync(versionStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException("Installer version.json is invalid.");

        profile["id"] = instanceName;
        if (!profile.ContainsKey("inheritsFrom") || string.IsNullOrWhiteSpace(profile["inheritsFrom"]?.GetValue<string>()))
        {
            profile["inheritsFrom"] = minecraftVersion;
        }

        if (!profile.ContainsKey("type"))
        {
            profile["type"] = "release";
        }

        Directory.CreateDirectory(instancePath);
        var versionJsonPath = Path.Combine(instancePath, instanceName + ".json");
        await File.WriteAllTextAsync(versionJsonPath, profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken)
            .ConfigureAwait(false);

        var files = new List<DownloadFile>();
        WriteInstallerJar(installerUrl, bytes, minecraftRootPath, files, sources);
        if (installProfile is not null
            && installProfile.TryGetPropertyValue("libraries", out var installerLibraries)
            && installerLibraries is JsonArray installerLibraryArray)
        {
            foreach (var library in installerLibraryArray.OfType<JsonObject>())
            {
                var file = CreateLibraryDownload(minecraftRootPath, library, sources, loaderName);
                if (file is not null)
                {
                    files.Add(file);
                }
            }

        }

        if (installProfile is not null)
        {
            files.AddRange(CreateDataReferenceDownloads(minecraftRootPath, installProfile, sources, loaderName));
        }

        if (profile.TryGetPropertyValue("libraries", out var libraries) && libraries is JsonArray libraryArray)
        {
            foreach (var library in libraryArray.OfType<JsonObject>())
            {
                var file = CreateLibraryDownload(minecraftRootPath, library, sources, loaderName);
                if (file is not null)
                {
                    files.Add(file);
                }
            }
        }

        var processors = installProfile is null
            ? []
            : ParseProcessors(installProfile, minecraftRootPath, instancePath, minecraftVersion);
        return new LoaderInstallPlan(loaderName, loaderVersion, versionJsonPath, files.DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase).ToList(), processors);
    }

    private static void WriteInstallerJar(
        string installerUrl,
        byte[] bytes,
        string minecraftRootPath,
        List<DownloadFile> files,
        IDownloadSourceService sources)
    {
        var mavenPath = TryGetMavenPathFromUrl(installerUrl);
        if (string.IsNullOrWhiteSpace(mavenPath))
        {
            return;
        }

        var localPath = Path.Combine(minecraftRootPath, "libraries", mavenPath);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        File.WriteAllBytes(localPath, bytes);
        files.Add(new DownloadFile(
            sources.GetLibrarySources(installerUrl),
            localPath,
            new DownloadFileCheck(ActualSize: bytes.LongLength),
            SimulateBrowserHeaders: true));
    }

    private static DownloadFile? CreateLibraryDownload(string minecraftRootPath, JsonObject library, IDownloadSourceService sources, string loaderName)
    {
        if (library.TryGetPropertyValue("downloads", out var downloadsNode)
            && downloadsNode is JsonObject downloads
            && downloads.TryGetPropertyValue("artifact", out var artifactNode)
            && artifactNode is JsonObject artifact)
        {
            var artifactUrl = artifact["url"]?.GetValue<string>() ?? "";
            var artifactPath = artifact["path"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(artifactUrl) || string.IsNullOrWhiteSpace(artifactPath))
            {
                return null;
            }

            return new DownloadFile(
                sources.GetLibrarySources(artifactUrl),
                Path.Combine(minecraftRootPath, "libraries", artifactPath.Replace('/', Path.DirectorySeparatorChar)),
                new DownloadFileCheck(ActualSize: artifact["size"]?.GetValue<long>() ?? -1, Hash: artifact["sha1"]?.GetValue<string>()),
                SimulateBrowserHeaders: true);
        }

        var name = library["name"]?.GetValue<string>() ?? "";
        var repository = library["url"]?.GetValue<string>() ?? GetDefaultRepository(loaderName);
        var mavenPath = CreateMavenPath(name);
        if (string.IsNullOrWhiteSpace(mavenPath))
        {
            return null;
        }

        var url = repository.TrimEnd('/') + "/" + mavenPath.Replace(Path.DirectorySeparatorChar, '/');
        return new DownloadFile(
            sources.GetLibrarySources(url),
            Path.Combine(minecraftRootPath, "libraries", mavenPath),
            new DownloadFileCheck(MinSize: 1),
            SimulateBrowserHeaders: true);
    }

    private static IReadOnlyList<LoaderProcessorStep> ParseProcessors(
        JsonObject installProfile,
        string minecraftRootPath,
        string instancePath,
        string minecraftVersion)
    {
        if (!installProfile.TryGetPropertyValue("processors", out var processorsNode) || processorsNode is not JsonArray processorsArray)
        {
            return [];
        }

        var replacements = BuildReplacements(installProfile, minecraftRootPath, instancePath, minecraftVersion);
        var result = new List<LoaderProcessorStep>();
        foreach (var processor in processorsArray.OfType<JsonObject>())
        {
            if (!RunsOnClient(processor))
            {
                continue;
            }

            var jar = ResolveArgument(processor["jar"]?.GetValue<string>() ?? "", replacements);
            var classpath = ReadStringArray(processor, "classpath")
                .Select(value => ResolveArgument(value, replacements))
                .ToList();
            var args = ReadStringArray(processor, "args")
                .Select(value => NormalizeInstallerPath(ResolveArgument(value, replacements), minecraftRootPath))
                .ToList();
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (processor.TryGetPropertyValue("outputs", out var outputsNode) && outputsNode is JsonObject outputObject)
            {
                foreach (var pair in outputObject)
                {
                    outputs[pair.Key] = NormalizeInstallerPath(ResolveArgument(pair.Value?.GetValue<string>() ?? "", replacements), minecraftRootPath);
                }
            }

            result.Add(new LoaderProcessorStep(jar, classpath, args, outputs, RunsOnClient(processor)));
        }

        return result;
    }

    private static Dictionary<string, string> BuildReplacements(
        JsonObject installProfile,
        string minecraftRootPath,
        string instancePath,
        string minecraftVersion)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{SIDE}"] = "client",
            ["{MINECRAFT_VERSION}"] = minecraftVersion,
            ["{MINECRAFT_JAR}"] = Path.Combine(minecraftRootPath, "versions", minecraftVersion, minecraftVersion + ".jar"),
            ["{ROOT}"] = minecraftRootPath,
            ["{INSTALLER}"] = instancePath,
            ["{LIBRARY_DIR}"] = Path.Combine(minecraftRootPath, "libraries")
        };

        if (installProfile.TryGetPropertyValue("data", out var dataNode) && dataNode is JsonObject data)
        {
            foreach (var pair in data)
            {
                var value = ReadDataValue(pair.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result["{" + pair.Key + "}"] = NormalizeInstallerPath(ResolveArgument(value, result), minecraftRootPath);
                }
            }
        }

        return result;
    }

    private static string NormalizeInstallerPath(string value, string minecraftRootPath)
    {
        var bracketCoordinate = TryGetBracketedMavenCoordinate(value);
        if (!string.IsNullOrWhiteSpace(bracketCoordinate))
        {
            var mavenPath = CreateMavenPath(bracketCoordinate);
            return string.IsNullOrWhiteSpace(mavenPath)
                ? value
                : Path.Combine(minecraftRootPath, "libraries", mavenPath);
        }

        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains("://", StringComparison.Ordinal)
            || IsMavenCoordinate(value))
        {
            return value;
        }

        var normalized = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.StartsWith("libraries" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("versions" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(minecraftRootPath, normalized);
        }

        return normalized;
    }

    private static IReadOnlyList<DownloadFile> CreateDataReferenceDownloads(
        string minecraftRootPath,
        JsonObject installProfile,
        IDownloadSourceService sources,
        string loaderName)
    {
        if (!installProfile.TryGetPropertyValue("data", out var dataNode) || dataNode is not JsonObject data)
        {
            return [];
        }

        var files = new List<DownloadFile>();
        foreach (var pair in data)
        {
            foreach (var coordinate in ExtractBracketedMavenCoordinates(ReadDataValue(pair.Value)))
            {
                var mavenPath = CreateMavenPath(coordinate);
                if (string.IsNullOrWhiteSpace(mavenPath))
                {
                    continue;
                }

                var repository = GetDefaultRepository(loaderName);
                var url = repository.TrimEnd('/') + "/" + mavenPath.Replace(Path.DirectorySeparatorChar, '/');
                files.Add(new DownloadFile(
                    sources.GetLibrarySources(url),
                    Path.Combine(minecraftRootPath, "libraries", mavenPath),
                    new DownloadFileCheck(MinSize: 1),
                    SimulateBrowserHeaders: true));
            }
        }

        return files;
    }

    private static IReadOnlyList<string> ExtractBracketedMavenCoordinates(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var result = new List<string>();
        var index = 0;
        while (index < value.Length)
        {
            var start = value.IndexOf('[', index);
            if (start < 0)
            {
                break;
            }

            var end = value.IndexOf(']', start + 1);
            if (end < 0)
            {
                break;
            }

            var coordinate = value[(start + 1)..end];
            if (IsMavenCoordinate(StripExtension(coordinate)))
            {
                result.Add(coordinate);
            }

            index = end + 1;
        }

        return result;
    }

    private static string TryGetBracketedMavenCoordinate(string value)
    {
        if (value.Length < 3 || value[0] != '[' || value[^1] != ']')
        {
            return "";
        }

        var coordinate = value[1..^1];
        return IsMavenCoordinate(StripExtension(coordinate)) ? coordinate : "";
    }

    private static string StripExtension(string coordinate)
    {
        var atIndex = coordinate.IndexOf('@', StringComparison.Ordinal);
        return atIndex >= 0 ? coordinate[..atIndex] : coordinate;
    }

    private static bool IsMavenCoordinate(string value)
    {
        var parts = value.Split(':');
        return parts.Length >= 3 && !value.Contains(Path.DirectorySeparatorChar) && !value.Contains('/');
    }

    private static string ReadDataValue(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }

        if (node is JsonValue value)
        {
            return value.GetValue<string>();
        }

        if (node is JsonObject obj)
        {
            return obj["client"]?.GetValue<string>()
                ?? obj["server"]?.GetValue<string>()
                ?? "";
        }

        return "";
    }

    private static bool RunsOnClient(JsonObject processor)
    {
        if (!processor.TryGetPropertyValue("sides", out var sidesNode) || sidesNode is not JsonArray sides)
        {
            return true;
        }

        return sides.OfType<JsonValue>()
            .Select(value => value.GetValue<string>())
            .Any(side => string.Equals(side, "client", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonArray array)
        {
            return [];
        }

        return array.OfType<JsonValue>().Select(value => value.GetValue<string>()).ToList();
    }

    private static string ResolveArgument(string value, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var replacement in replacements)
        {
            value = value.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    private static string GetDefaultRepository(string loaderName)
    {
        return string.Equals(loaderName, "neoforge", StringComparison.OrdinalIgnoreCase)
            ? "https://maven.neoforged.net/releases/"
            : "https://maven.minecraftforge.net/";
    }

    private static string TryGetMavenPathFromUrl(string url)
    {
        foreach (var prefix in new[]
                 {
                     "https://maven.minecraftforge.net/",
                     "https://maven.neoforged.net/releases/"
                 })
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return url[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            }
        }

        return "";
    }

    private static string CreateMavenPath(string coordinate)
    {
        var extension = "jar";
        var atIndex = coordinate.IndexOf('@', StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            extension = coordinate[(atIndex + 1)..];
            coordinate = coordinate[..atIndex];
        }

        var parts = coordinate.Split(':');
        if (parts.Length < 3)
        {
            return "";
        }

        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return Path.Combine(group, artifact, version, $"{artifact}-{version}{classifier}.{extension}");
    }
}
