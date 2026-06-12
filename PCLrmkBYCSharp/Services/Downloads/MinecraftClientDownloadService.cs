using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class MinecraftClientDownloadService(
    IDownloadByteClient client,
    IDownloadSourceService sources,
    IAppSettingsService settings) : IMinecraftClientDownloadService
{
    private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    public async Task<IReadOnlyList<MinecraftRemoteVersion>> GetVersionManifestAsync(CancellationToken cancellationToken = default)
    {
        var versionSourceMode = settings.Get(AppSettingKeys.ToolDownloadVersion, 1);
        var preferOfficial = versionSourceMode == 2 || (versionSourceMode == 1 && sources.PreferOfficialDownloadsWhenAuto);
        var candidates = preferOfficial
            ? new[] { ManifestUrl, ToBmclManifest(ManifestUrl) }
            : new[] { ToBmclManifest(ManifestUrl), ManifestUrl };
        Exception? last = null;
        foreach (var url in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var bytes = await client.GetBytesAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                using var document = JsonDocument.Parse(bytes);
                var versions = new List<MinecraftRemoteVersion>();
                foreach (var version in document.RootElement.GetProperty("versions").EnumerateArray())
                {
                    versions.Add(new MinecraftRemoteVersion(
                        GetString(version, "id"),
                        GetString(version, "type"),
                        DateTimeOffset.TryParse(GetString(version, "releaseTime"), out var releaseTime) ? releaseTime : DateTimeOffset.MinValue,
                        GetString(version, "url"),
                        url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase) ? "BMCLAPI" : "Mojang"));
                }

                if (!url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase))
                {
                    sources.ReportOfficialVersionListLatency(stopwatch.Elapsed);
                }

                return versions
                    .Where(version => !string.IsNullOrWhiteSpace(version.Id) && !string.IsNullOrWhiteSpace(version.Url))
                    .OrderByDescending(version => version.ReleaseTime)
                    .ThenBy(version => version.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        throw new HttpRequestException("获取 Minecraft 版本列表失败", last);
    }

    public async Task<MinecraftClientInstallPlan> CreateInstallPlanAsync(string minecraftRootPath, string versionId, string instanceName, CancellationToken cancellationToken = default)
    {
        var version = (await GetVersionManifestAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未找到 Minecraft 版本：" + versionId);
        var versionJsonBytes = await FetchBytesAsync(sources.GetLauncherOrMetaSources(version.Url), "版本 json", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(versionJsonBytes);
        var root = document.RootElement;
        instanceName = string.IsNullOrWhiteSpace(instanceName) ? versionId : instanceName.Trim();
        var versionFolder = Path.Combine(minecraftRootPath, "versions", instanceName);
        var jsonPath = Path.Combine(versionFolder, instanceName + ".json");
        var files = new List<DownloadFile>
        {
            new(
                sources.GetLauncherOrMetaSources(version.Url),
                jsonPath,
                new DownloadFileCheck(IsJson: true))
        };

        if (root.TryGetProperty("downloads", out var downloads)
            && downloads.TryGetProperty("client", out var clientDownload)
            && clientDownload.TryGetProperty("url", out var jarUrl))
        {
            files.Add(new DownloadFile(
                sources.GetLauncherOrMetaSources(jarUrl.GetString() ?? ""),
                Path.Combine(versionFolder, instanceName + ".jar"),
                new DownloadFileCheck(MinSize: 1024, ActualSize: GetLong(clientDownload, "size"), Hash: GetString(clientDownload, "sha1"))));
        }

        if (root.TryGetProperty("assetIndex", out var assetIndex)
            && assetIndex.TryGetProperty("url", out var assetUrl)
            && assetIndex.TryGetProperty("id", out var assetId))
        {
            var assetIndexUrl = assetUrl.GetString() ?? "";
            var assetIndexPath = Path.Combine(minecraftRootPath, "assets", "indexes", assetId.GetString() + ".json");
            files.Add(new DownloadFile(
                sources.GetLauncherOrMetaSources(assetIndexUrl),
                assetIndexPath,
                new DownloadFileCheck(ActualSize: GetLong(assetIndex, "size"), Hash: GetString(assetIndex, "sha1"), IsJson: true)));
            await AddAssetObjectsAsync(minecraftRootPath, versionFolder, assetIndexUrl, files, cancellationToken).ConfigureAwait(false);
        }

        AddLibraries(minecraftRootPath, root, files);

        return new MinecraftClientInstallPlan(
            versionId,
            instanceName,
            versionFolder,
            files.DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private async Task AddAssetObjectsAsync(string minecraftRootPath, string versionFolder, string assetIndexUrl, List<DownloadFile> files, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetIndexUrl))
        {
            return;
        }

        var bytes = await FetchBytesAsync(sources.GetLauncherOrMetaSources(assetIndexUrl), "资源索引", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var mapToResources = GetBool(document.RootElement, "map_to_resources");
        var isVirtual = GetBool(document.RootElement, "virtual");
        foreach (var item in objects.EnumerateObject())
        {
            var hash = GetString(item.Value, "hash");
            if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2)
            {
                continue;
            }

            var localPath = GetAssetLocalPath(minecraftRootPath, versionFolder, item.Name, hash, mapToResources, isVirtual);
            files.Add(new DownloadFile(
                sources.GetAssetSources($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"),
                localPath,
                new DownloadFileCheck(ActualSize: GetLong(item.Value, "size"), Hash: hash)));
        }
    }

    private void AddLibraries(string minecraftRootPath, JsonElement root, List<DownloadFile> files)
    {
        if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var library in libraries.EnumerateArray())
        {
            if (library.ValueKind != JsonValueKind.Object || !CheckRules(library))
            {
                continue;
            }

            files.AddRange(CreateLibraryFiles(minecraftRootPath, library));
        }
    }

    private IEnumerable<DownloadFile> CreateLibraryFiles(string minecraftRootPath, JsonElement library)
    {
        var name = GetString(library, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            yield break;
        }

        JsonElement artifact = default;
        var hasArtifact = library.TryGetProperty("downloads", out var downloadsNode)
            && downloadsNode.TryGetProperty("artifact", out artifact);
        if (hasArtifact)
        {
            yield return CreateLibraryFile(minecraftRootPath, name, artifact, null, library);
        }

        if (library.TryGetProperty("natives", out var natives)
            && natives.TryGetProperty("windows", out var windowsNative)
            && windowsNative.ValueKind == JsonValueKind.String)
        {
            var classifierName = windowsNative.GetString()!
                .Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
            if (library.TryGetProperty("downloads", out var downloads)
                && downloads.TryGetProperty("classifiers", out var classifiers)
                && classifiers.TryGetProperty(classifierName, out var classifier))
            {
                yield return CreateLibraryFile(minecraftRootPath, name, classifier, classifierName, library);
            }
            else
            {
                yield return CreateLibraryFileFromName(minecraftRootPath, name, classifierName, library);
            }

            yield break;
        }

        if (!hasArtifact)
        {
            yield return CreateLibraryFileFromName(minecraftRootPath, name, null, library);
        }
    }

    private DownloadFile CreateLibraryFile(string minecraftRootPath, string name, JsonElement artifact, string? classifier, JsonElement library)
    {
        var relativePath = GetString(artifact, "path");
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = GetMavenRelativePath(name, classifier);
        }

        var localPath = Path.Combine(minecraftRootPath, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var originalUrl = GetString(artifact, "url");
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            originalUrl = BuildLibraryUrl(library, relativePath);
        }

        var hash = name.Contains("labymod", StringComparison.OrdinalIgnoreCase) ? null : GetString(artifact, "sha1");
        return new DownloadFile(
            sources.GetLibrarySources(originalUrl),
            localPath,
            new DownloadFileCheck(ActualSize: GetLong(artifact, "size"), Hash: hash));
    }

    private DownloadFile CreateLibraryFileFromName(string minecraftRootPath, string name, string? classifier, JsonElement library)
    {
        var relativePath = GetMavenRelativePath(name, classifier);
        var localPath = Path.Combine(minecraftRootPath, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var url = BuildLibraryUrl(library, relativePath);
        return new DownloadFile(sources.GetLibrarySources(url), localPath, new DownloadFileCheck());
    }

    private string BuildLibraryUrl(JsonElement library, string relativePath)
    {
        var rootUrl = GetString(library, "url");
        if (!string.IsNullOrWhiteSpace(rootUrl))
        {
            return rootUrl.TrimEnd('/') + "/" + relativePath;
        }

        return "https://libraries.minecraft.net/" + relativePath;
    }

    private async Task<byte[]> FetchBytesAsync(IEnumerable<string> candidateUrls, string description, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var url in candidateUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return await client.GetBytesAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        throw new HttpRequestException("获取 Minecraft " + description + " 失败", last);
    }

    private static string GetAssetLocalPath(string minecraftRootPath, string versionFolder, string sourcePath, string hash, bool mapToResources, bool isVirtual)
    {
        var relative = sourcePath.Replace('/', Path.DirectorySeparatorChar);
        if (mapToResources)
        {
            return Path.Combine(versionFolder, "resources", relative);
        }

        if (isVirtual)
        {
            return Path.Combine(minecraftRootPath, "assets", "virtual", "legacy", relative);
        }

        return Path.Combine(minecraftRootPath, "assets", "objects", hash[..2], hash);
    }

    private static bool CheckRules(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var applies = true;
            if (rule.TryGetProperty("os", out var os))
            {
                applies = OsRuleMatches(os);
            }

            if (applies)
            {
                allowed = string.Equals(GetString(rule, "action"), "allow", StringComparison.OrdinalIgnoreCase);
            }
        }

        return allowed;
    }

    private static bool OsRuleMatches(JsonElement os)
    {
        if (os.TryGetProperty("name", out var name)
            && !string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (os.TryGetProperty("arch", out var arch) && arch.ValueKind == JsonValueKind.String)
        {
            var expected = arch.GetString() ?? "";
            if (!CurrentArchitectureAliases().Contains(expected, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var versionPattern = GetString(os, "version");
        if (!string.IsNullOrWhiteSpace(versionPattern) && !OsVersionMatches(versionPattern))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> CurrentArchitectureAliases()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => ["x64", "amd64"],
            Architecture.X86 => ["x86", "i386"],
            Architecture.Arm64 => ["arm64", "aarch64"],
            Architecture.Arm => ["arm"],
            _ => [RuntimeInformation.OSArchitecture.ToString()]
        };
    }

    private static bool OsVersionMatches(string pattern)
    {
        try
        {
            var version = Environment.OSVersion.Version.ToString();
            var versionString = Environment.OSVersion.VersionString;
            return Regex.IsMatch(version, pattern, RegexOptions.IgnoreCase)
                || Regex.IsMatch(versionString, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string GetMavenRelativePath(string name, string? classifier)
    {
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            return "";
        }

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var suffix = string.IsNullOrWhiteSpace(classifier)
            ? parts.Length > 3 ? "-" + parts[3] : ""
            : "-" + classifier;
        return $"{group}/{artifact}/{version}/{artifact}-{version}{suffix}.jar";
    }

    private static string ToBmclManifest(string url)
    {
        return url.Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number) ? number : -1;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }
}
