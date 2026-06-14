using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchFileCompleter : ILaunchFileCompleter
{
    private readonly IDownloadSourceService _sources;
    private readonly IFileCheckService _checker;
    private readonly IAppLoggerService _logger;
    private readonly ILaunchHttpClient _http;
    private readonly string? _pureDirectory;

    public LaunchFileCompleter(IDownloadSourceService? sources = null, IFileCheckService? checker = null, IAppLoggerService? logger = null, ILaunchHttpClient? http = null, string? pureDirectory = null)
    {
        _logger = logger ?? new SilentLoggerService();
        _checker = checker ?? new FileCheckService(_logger);
        _sources = sources ?? new OfficialFirstDownloadSourceService();
        _http = http ?? new LaunchHttpClient();
        _pureDirectory = pureDirectory;
    }

    public async Task<IReadOnlyList<string>> CheckMissingFilesAsync(LaunchRequest request, IReadOnlyList<string> argumentMissingFiles, CancellationToken cancellationToken = default)
    {
        return (await BuildCompletionPlanAsync(request, argumentMissingFiles, cancellationToken).ConfigureAwait(false)).MissingFiles;
    }

    public async Task<LaunchFileCompletionResult> BuildCompletionPlanAsync(LaunchRequest request, IReadOnlyList<string> argumentMissingFiles, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var missing = new HashSet<string>(argumentMissingFiles.Where(path => !File.Exists(path)), StringComparer.OrdinalIgnoreCase);
        var downloads = new List<DownloadFile>();
        if (request.Instance is null)
        {
            return CreateResult(missing, downloads);
        }

        var documents = LoadVersionDocuments(request.Instance);
        foreach (var document in documents)
        {
            AddClientJar(document, downloads, missing);
            AddLibraries(request.MinecraftRootPath, document.Root, downloads, missing);
            AddLoggingConfig(request.MinecraftRootPath, document.Root, downloads, missing);
        }

        AddAssets(request, documents, downloads, missing);
        await AddThirdPartyLoginAgentsAsync(request, downloads, missing, cancellationToken).ConfigureAwait(false);
        return CreateResult(missing, downloads);
    }

    private async Task AddThirdPartyLoginAgentsAsync(LaunchRequest request, List<DownloadFile> downloads, HashSet<string> missing, CancellationToken cancellationToken)
    {
        if (request.Instance is null)
        {
            return;
        }

        if (request.LoginType == LoginType.Nide)
        {
            var target = Path.Combine(ResolvePureDirectory(request), "nide8auth.jar");
            var hash = await TryGetNideJarHashAsync(request.LoginServer, cancellationToken).ConfigureAwait(false);
            AddAgentDownload(target, string.IsNullOrWhiteSpace(hash) ? [] : ["https://login.mc-user.com:233/index/jar"], hash, downloads, missing);
        }
        else if (request.LoginType == LoginType.Auth)
        {
            var target = Path.Combine(ResolvePureDirectory(request), "authlib-injector.jar");
            var info = await TryGetAuthlibInjectorDownloadAsync(cancellationToken).ConfigureAwait(false);
            AddAgentDownload(target, info.Sources, info.Hash, downloads, missing);
        }
    }

    private string ResolvePureDirectory(LaunchRequest request)
    {
        var path = string.IsNullOrWhiteSpace(_pureDirectory)
            ? request.Instance!.VersionPath
            : _pureDirectory;
        return Path.GetFullPath(path);
    }

    private void AddAgentDownload(string target, IReadOnlyList<string> sources, string? hash, List<DownloadFile> downloads, HashSet<string> missing)
    {
        var check = new DownloadFileCheck(MinSize: 1024, Hash: hash);
        if (sources.Count > 0)
        {
            downloads.Add(new DownloadFile(sources, target, check));
        }

        if (File.Exists(target) && _checker.Check(target, sources.Count > 0 ? check : new DownloadFileCheck(MinSize: 1024)) is null)
        {
            missing.Remove(target);
            return;
        }

        missing.Add(target);
    }

    private async Task<string?> TryGetNideJarHashAsync(string serverId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return null;
        }

        try
        {
            var text = await _http.SendAsync(new LaunchHttpRequest(
                $"https://auth.mc-user.com:233/{serverId.Trim().Trim('/')}",
                HttpMethod.Get), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            return GetString(document.RootElement, "jarHash");
        }
        catch (Exception ex)
        {
            _logger.Warn("获取统一通行证下载信息失败：" + ex.Message);
            return null;
        }
    }

    private async Task<(IReadOnlyList<string> Sources, string? Hash)> TryGetAuthlibInjectorDownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = await _http.SendAsync(new LaunchHttpRequest(
                "https://authlib-injector.yushi.moe/artifact/latest.json",
                HttpMethod.Get), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            var downloadUrl = GetString(document.RootElement, "download_url")
                .Replace("bmclapi2.bangbang93.com/mirrors/authlib-injector", "authlib-injector.yushi.moe", StringComparison.OrdinalIgnoreCase);
            var hash = "";
            if (document.RootElement.TryGetProperty("checksums", out var checksums))
            {
                hash = GetString(checksums, "sha256");
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return ([], hash);
            }

            return ([
                downloadUrl,
                downloadUrl.Replace("authlib-injector.yushi.moe", "bmclapi2.bangbang93.com/mirrors/authlib-injector", StringComparison.OrdinalIgnoreCase)
            ], hash);
        }
        catch (Exception ex)
        {
            _logger.Warn("获取 Authlib-Injector 下载信息失败：" + ex.Message);
            return ([], null);
        }
    }

    private LaunchFileCompletionResult CreateResult(HashSet<string> missing, List<DownloadFile> downloads)
    {
        var distinctDownloads = downloads
            .Where(file => file.Sources.Count > 0)
            .DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in distinctDownloads)
        {
            if (_checker.Check(file.LocalPath, file.Check) is not null)
            {
                missing.Add(file.LocalPath);
            }
            else
            {
                missing.Remove(file.LocalPath);
            }
        }

        return new LaunchFileCompletionResult(
            missing.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
            distinctDownloads);
    }

    private IReadOnlyList<VersionDocument> LoadVersionDocuments(MinecraftInstance instance)
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

    private void AddClientJar(VersionDocument document, List<DownloadFile> downloads, HashSet<string> missing)
    {
        if (!document.Root.TryGetProperty("downloads", out var downloadsNode)
            || !downloadsNode.TryGetProperty("client", out var client)
            || !client.TryGetProperty("url", out var url)
            || url.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var localPath = Path.Combine(document.VersionPath, document.Name + ".jar");
        var check = new DownloadFileCheck(MinSize: 1024, ActualSize: GetLong(client, "size"), Hash: GetString(client, "sha1"));
        downloads.Add(new DownloadFile(_sources.GetLauncherOrMetaSources(url.GetString() ?? ""), localPath, check));
        if (_checker.Check(localPath, check) is not null)
        {
            missing.Add(localPath);
        }
    }

    private void AddLibraries(string minecraftRootPath, JsonElement root, List<DownloadFile> downloads, HashSet<string> missing)
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

            foreach (var file in CreateLibraryFiles(minecraftRootPath, library))
            {
                downloads.Add(file);
                if (_checker.Check(file.LocalPath, file.Check) is not null)
                {
                    missing.Add(file.LocalPath);
                }
            }
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
            yield return CreateLibraryFile(minecraftRootPath, name, artifact, null, false, library);
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
                yield return CreateLibraryFile(minecraftRootPath, name, classifier, classifierName, true, library);
            }
            else
            {
                yield return CreateLibraryFileFromName(minecraftRootPath, name, classifierName, library);
            }

            yield break;
        }

        if (hasArtifact)
        {
            yield break;
        }

        yield return CreateLibraryFileFromName(minecraftRootPath, name, null, library);
    }

    private void AddLoggingConfig(string minecraftRootPath, JsonElement root, List<DownloadFile> downloads, HashSet<string> missing)
    {
        if (!root.TryGetProperty("logging", out var logging)
            || !logging.TryGetProperty("client", out var client)
            || !client.TryGetProperty("file", out var file)
            || file.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = GetString(file, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var localPath = Path.Combine(minecraftRootPath, "assets", "log_configs", id);
        var check = new DownloadFileCheck(ActualSize: GetLong(file, "size"), Hash: GetString(file, "sha1"));
        var url = GetString(file, "url");
        if (!string.IsNullOrWhiteSpace(url))
        {
            downloads.Add(new DownloadFile(_sources.GetLauncherOrMetaSources(url), localPath, check));
        }

        if (_checker.Check(localPath, check) is not null)
        {
            missing.Add(localPath);
        }
    }

    private DownloadFile CreateLibraryFile(string minecraftRootPath, string name, JsonElement artifact, string? classifier, bool isNative, JsonElement library)
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
        var check = new DownloadFileCheck(ActualSize: GetLong(artifact, "size"), Hash: hash);
        return new DownloadFile(_sources.GetLibrarySources(originalUrl), localPath, check);
    }

    private DownloadFile CreateLibraryFileFromName(string minecraftRootPath, string name, string? classifier, JsonElement library)
    {
        var relativePath = GetMavenRelativePath(name, classifier);
        var localPath = Path.Combine(minecraftRootPath, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var url = BuildLibraryUrl(library, relativePath);
        if (string.IsNullOrWhiteSpace(url) && string.Equals(name, "net.minecraftforge:forge:universal", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://maven.minecraftforge.net/" + relativePath;
        }

        return new DownloadFile(_sources.GetLibrarySources(url), localPath, new DownloadFileCheck());
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

    private void AddAssets(LaunchRequest request, IReadOnlyList<VersionDocument> documents, List<DownloadFile> downloads, HashSet<string> missing)
    {
        var index = FindAssetIndex(documents);
        if (index is null)
        {
            _logger.Warn("未找到资源文件索引信息，暂无法补全 assets");
            return;
        }

        var indexPath = Path.Combine(request.MinecraftRootPath, "assets", "indexes", index.Id + ".json");
        var indexCheck = new DownloadFileCheck(ActualSize: index.Size, Hash: index.Sha1, IsJson: true);
        if (!string.IsNullOrWhiteSpace(index.Url))
        {
            downloads.Add(new DownloadFile(_sources.GetLauncherOrMetaSources(index.Url), indexPath, indexCheck));
        }

        if (_checker.Check(indexPath, indexCheck) is not null)
        {
            missing.Add(indexPath);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
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

                var localPath = GetAssetLocalPath(request, item.Name, hash, mapToResources, isVirtual);
                var check = new DownloadFileCheck(ActualSize: GetLong(item.Value, "size"), Hash: hash);
                downloads.Add(new DownloadFile(
                    _sources.GetAssetSources($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"),
                    localPath,
                    check));
                if (_checker.Check(localPath, check) is not null)
                {
                    missing.Add(localPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("解析资源文件索引失败：" + ex.Message);
            missing.Add(indexPath);
        }
    }

    private static string GetAssetLocalPath(LaunchRequest request, string sourcePath, string hash, bool mapToResources, bool isVirtual)
    {
        var relative = sourcePath.Replace('/', Path.DirectorySeparatorChar);
        if (mapToResources)
        {
            return Path.Combine(request.Instance!.VersionPath, "resources", relative);
        }

        if (isVirtual)
        {
            return Path.Combine(request.MinecraftRootPath, "assets", "virtual", "legacy", relative);
        }

        return Path.Combine(request.MinecraftRootPath, "assets", "objects", hash[..2], hash);
    }

    private static AssetIndexInfo? FindAssetIndex(IReadOnlyList<VersionDocument> documents)
    {
        foreach (var document in documents)
        {
            if (document.Root.TryGetProperty("assetIndex", out var index)
                && index.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                return new AssetIndexInfo(
                    id.GetString() ?? "legacy",
                    GetString(index, "url"),
                    GetString(index, "sha1"),
                    GetLong(index, "size"));
            }

            var assets = GetString(document.Root, "assets");
            if (!string.IsNullOrWhiteSpace(assets))
            {
                return new AssetIndexInfo(assets, "", "", -1);
            }
        }

        return new AssetIndexInfo(
            "legacy",
            "https://launchermeta.mojang.com/mc-staging/assets/legacy/c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729/legacy.json",
            "c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729",
            134284);
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

    private sealed record VersionDocument(string Name, string VersionPath, string JsonPath, JsonElement Root);

    private sealed record AssetIndexInfo(string Id, string Url, string Sha1, long Size);

    private sealed class SilentLoggerService : IAppLoggerService
    {
        public void Initialize()
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }

    private sealed class OfficialFirstDownloadSourceService : IDownloadSourceService
    {
        public bool PreferOfficialDownloadsWhenAuto => true;

        public IReadOnlyList<string> OrderSources(IEnumerable<string> officialUrls, IEnumerable<string> mirrorUrls)
        {
            return officialUrls.Concat(mirrorUrls).Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public IReadOnlyList<string> GetLauncherOrMetaSources(string original)
        {
            return OrderSources([original], []);
        }

        public IReadOnlyList<string> GetLibrarySources(string original)
        {
            return OrderSources([original], []);
        }

        public IReadOnlyList<string> GetAssetSources(string original)
        {
            return OrderSources([original], []);
        }

        public string GetModMirrorSource(string original)
        {
            return original;
        }

        public IReadOnlyList<string> GetModFileSources(string original)
        {
            return [original];
        }

        public void ReportOfficialVersionListLatency(TimeSpan elapsed)
        {
        }
    }
}
