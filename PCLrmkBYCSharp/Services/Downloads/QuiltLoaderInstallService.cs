using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class QuiltLoaderInstallService(
    IDownloadByteClient client,
    IDownloadSourceService sources,
    IAppLoggerService logger) : IQuiltLoaderInstallService
{
    public async Task<LoaderInstallPlan> CreateInstallPlanAsync(
        string minecraftRootPath,
        string instanceName,
        string instancePath,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new ArgumentException("Minecraft version is required.", nameof(minecraftVersion));
        }

        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            throw new ArgumentException("Quilt loader version is required.", nameof(loaderVersion));
        }

        var url = BuildProfileUrl(minecraftVersion, loaderVersion);
        logger.Info("Fetching Quilt loader profile: " + url);
        var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        var profile = JsonNode.Parse(bytes)?.AsObject()
            ?? throw new InvalidDataException("Invalid Quilt loader profile.");

        profile["id"] = instanceName;
        profile["inheritsFrom"] = minecraftVersion;
        Directory.CreateDirectory(instancePath);
        var versionJsonPath = Path.Combine(instancePath, instanceName + ".json");
        await File.WriteAllTextAsync(versionJsonPath, profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken)
            .ConfigureAwait(false);

        var files = new List<DownloadFile>();
        if (profile.TryGetPropertyValue("libraries", out var libraries) && libraries is JsonArray libraryArray)
        {
            foreach (var library in libraryArray.OfType<JsonObject>())
            {
                var file = CreateLibraryDownload(minecraftRootPath, library);
                if (file is not null)
                {
                    files.Add(file);
                }
            }
        }

        return new LoaderInstallPlan("quilt-loader", loaderVersion, versionJsonPath, files.DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase).ToList(), []);
    }

    public static string BuildProfileUrl(string minecraftVersion, string loaderVersion)
    {
        return "https://meta.quiltmc.org/v3/versions/loader/"
            + Uri.EscapeDataString(minecraftVersion.Trim())
            + "/"
            + Uri.EscapeDataString(loaderVersion.Trim())
            + "/profile/json";
    }

    private DownloadFile? CreateLibraryDownload(string minecraftRootPath, JsonObject library)
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
        var repository = library["url"]?.GetValue<string>() ?? "https://maven.quiltmc.org/repository/release/";
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
