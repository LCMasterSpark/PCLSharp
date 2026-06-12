using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class ModpackInstallService(
    IMinecraftClientDownloadService minecraftClientDownload,
    IFabricLoaderInstallService fabricLoaderInstall,
    IQuiltLoaderInstallService quiltLoaderInstall,
    IForgeLoaderInstallService forgeLoaderInstall,
    INeoForgeLoaderInstallService neoForgeLoaderInstall,
    IDownloadSourceService sources,
    IAppLoggerService logger,
    IDownloadByteClient? client = null) : IModpackInstallService
{
    public async Task<ModpackInstallPlan> CreateModrinthInstallPlanAsync(
        string modpackPath,
        string minecraftRootPath,
        string? instanceName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modpackPath) || !File.Exists(modpackPath))
        {
            throw new FileNotFoundException("\u672a\u627e\u5230\u6574\u5408\u5305\u6587\u4ef6", modpackPath);
        }

        if (string.IsNullOrWhiteSpace(minecraftRootPath))
        {
            throw new ArgumentException("Minecraft \u6839\u76ee\u5f55\u4e0d\u80fd\u4e3a\u7a7a", nameof(minecraftRootPath));
        }

        using var archive = ZipFile.OpenRead(modpackPath);
        var indexEntry = archive.GetEntry("modrinth.index.json")
            ?? null;
        if (indexEntry is null)
        {
            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new InvalidDataException("整合包缺少 modrinth.index.json 或 manifest.json");
            return await CreateCurseForgeInstallPlanAsync(
                archive,
                manifestEntry,
                modpackPath,
                minecraftRootPath,
                instanceName,
                cancellationToken).ConfigureAwait(false);
        }

        using var indexStream = indexEntry.Open();
        using var document = await JsonDocument.ParseAsync(indexStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var packName = GetString(root, "name", Path.GetFileNameWithoutExtension(modpackPath));
        var safeInstanceName = SanitizeInstanceName(string.IsNullOrWhiteSpace(instanceName) ? packName : instanceName!);
        var dependencies = root.TryGetProperty("dependencies", out var dependencyElement) && dependencyElement.ValueKind == JsonValueKind.Object
            ? dependencyElement
            : default;
        var minecraftVersion = GetString(dependencies, "minecraft");
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidDataException("\u6574\u5408\u5305\u7f3a\u5c11 Minecraft \u7248\u672c\u4f9d\u8d56");
        }

        var loader = GetLoader(dependencies);
        var basePlan = await minecraftClientDownload.CreateInstallPlanAsync(minecraftRootPath, minecraftVersion, safeInstanceName, cancellationToken)
            .ConfigureAwait(false);
        var files = new List<DownloadFile>(basePlan.Files);
        var warnings = new List<string>();
        var processors = new List<LoaderProcessorStep>();
        if (!await TryAddLoaderInstallAsync(
            loader,
            minecraftRootPath,
            minecraftVersion,
            safeInstanceName,
            basePlan.VersionFolder,
            files,
            processors,
            cancellationToken).ConfigureAwait(false)
            && loader.Name is not null)
        {
            warnings.Add($"\u5df2\u8bc6\u522b {loader.Name} {loader.Version}\uff0c\u4f46\u6682\u4e0d\u652f\u6301\u81ea\u52a8\u5b89\u88c5\u8be5\u52a0\u8f7d\u5668");
        }

        if (root.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                if (!ShouldInstallOnClient(file))
                {
                    continue;
                }

                var download = CreatePackDownloadFile(file, basePlan.VersionFolder);
                if (download is not null)
                {
                    files.Add(download);
                }
            }
        }

        var overrideCount = ExtractOverrides(archive, basePlan.VersionFolder);
        WritePackMetadata(basePlan.VersionFolder, root);

        logger.Info($"\u5df2\u521b\u5efa Modrinth \u6574\u5408\u5305\u5b89\u88c5\u8ba1\u5212\uff1a{packName} / {safeInstanceName}");
        return new ModpackInstallPlan(
            packName,
            safeInstanceName,
            minecraftVersion,
            loader.Name,
            loader.Version,
            basePlan.VersionFolder,
            files.DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase).ToList(),
            warnings,
            processors,
            overrideCount);
    }

    private async Task<ModpackInstallPlan> CreateCurseForgeInstallPlanAsync(
        ZipArchive archive,
        ZipArchiveEntry manifestEntry,
        string modpackPath,
        string minecraftRootPath,
        string? instanceName,
        CancellationToken cancellationToken)
    {
        using var manifestStream = manifestEntry.Open();
        using var document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var packName = GetString(root, "name", Path.GetFileNameWithoutExtension(modpackPath));
        var safeInstanceName = SanitizeInstanceName(string.IsNullOrWhiteSpace(instanceName) ? packName : instanceName!);
        var minecraft = root.TryGetProperty("minecraft", out var minecraftElement) && minecraftElement.ValueKind == JsonValueKind.Object
            ? minecraftElement
            : default;
        var minecraftVersion = GetString(minecraft, "version");
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidDataException("CurseForge 整合包缺少 Minecraft 版本信息");
        }

        var loader = GetCurseForgeLoader(minecraft);
        var basePlan = await minecraftClientDownload.CreateInstallPlanAsync(minecraftRootPath, minecraftVersion, safeInstanceName, cancellationToken)
            .ConfigureAwait(false);
        var files = new List<DownloadFile>(basePlan.Files);
        var warnings = new List<string>();
        var processors = new List<LoaderProcessorStep>();
        if (!await TryAddLoaderInstallAsync(
            loader,
            minecraftRootPath,
            minecraftVersion,
            safeInstanceName,
            basePlan.VersionFolder,
            files,
            processors,
            cancellationToken).ConfigureAwait(false)
            && loader.Name is not null)
        {
            warnings.Add($"已识别 {loader.Name} {loader.Version}，但暂不支持自动安装该加载器");
        }

        if (root.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                var download = await CreateCurseForgePackDownloadFileAsync(file, basePlan.VersionFolder, warnings, cancellationToken)
                    .ConfigureAwait(false);
                if (download is not null)
                {
                    files.Add(download);
                }
            }
        }

        var overridesFolder = GetString(root, "overrides", "overrides");
        var overrideCount = ExtractOverrides(archive, basePlan.VersionFolder, overridesFolder);
        WriteCurseForgePackMetadata(basePlan.VersionFolder, root);

        logger.Info($"已创建 CurseForge 整合包安装计划：{packName} / {safeInstanceName}");
        return new ModpackInstallPlan(
            packName,
            safeInstanceName,
            minecraftVersion,
            loader.Name,
            loader.Version,
            basePlan.VersionFolder,
            files.DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase).ToList(),
            warnings,
            processors,
            overrideCount);
    }

    private async Task<bool> TryAddLoaderInstallAsync(
        (string? Name, string? Version) loader,
        string minecraftRootPath,
        string minecraftVersion,
        string instanceName,
        string versionFolder,
        List<DownloadFile> files,
        List<LoaderProcessorStep> processors,
        CancellationToken cancellationToken)
    {
        if (loader.Name is null)
        {
            return true;
        }

        LoaderInstallPlan? loaderPlan = null;
        if (string.Equals(loader.Name, "fabric-loader", StringComparison.OrdinalIgnoreCase))
        {
            loaderPlan = await fabricLoaderInstall.CreateInstallPlanAsync(
                minecraftRootPath,
                instanceName,
                versionFolder,
                minecraftVersion,
                loader.Version ?? "",
                cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(loader.Name, "quilt-loader", StringComparison.OrdinalIgnoreCase))
        {
            loaderPlan = await quiltLoaderInstall.CreateInstallPlanAsync(
                minecraftRootPath,
                instanceName,
                versionFolder,
                minecraftVersion,
                loader.Version ?? "",
                cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(loader.Name, "forge", StringComparison.OrdinalIgnoreCase))
        {
            loaderPlan = await forgeLoaderInstall.CreateInstallPlanAsync(
                minecraftRootPath,
                instanceName,
                versionFolder,
                minecraftVersion,
                loader.Version ?? "",
                cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(loader.Name, "neoforge", StringComparison.OrdinalIgnoreCase))
        {
            loaderPlan = await neoForgeLoaderInstall.CreateInstallPlanAsync(
                minecraftRootPath,
                instanceName,
                versionFolder,
                minecraftVersion,
                loader.Version ?? "",
                cancellationToken).ConfigureAwait(false);
        }

        if (loaderPlan is null)
        {
            return false;
        }

        var inheritedBasePlan = await minecraftClientDownload.CreateInstallPlanAsync(minecraftRootPath, minecraftVersion, minecraftVersion, cancellationToken)
            .ConfigureAwait(false);
        files.AddRange(inheritedBasePlan.Files);
        RemoveVersionJsonDownload(files, versionFolder, instanceName);
        files.AddRange(loaderPlan.Files);
        processors.AddRange(loaderPlan.Processors);
        return true;
    }

    private static void RemoveVersionJsonDownload(List<DownloadFile> files, string versionFolder, string instanceName)
    {
        var versionJsonPath = Path.Combine(versionFolder, instanceName + ".json");
        files.RemoveAll(file => string.Equals(file.LocalPath, versionJsonPath, StringComparison.OrdinalIgnoreCase));
    }

    private DownloadFile? CreatePackDownloadFile(JsonElement file, string instancePath)
    {
        var relativePath = GetString(file, "path");
        if (string.IsNullOrWhiteSpace(relativePath) || !IsSafeRelativePath(relativePath))
        {
            return null;
        }

        if (!file.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var primaryUrl = downloads.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        if (string.IsNullOrWhiteSpace(primaryUrl))
        {
            return null;
        }

        string? sha1 = null;
        if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object)
        {
            sha1 = GetString(hashes, "sha1");
        }

        var localPath = Path.Combine(instancePath, NormalizePackPath(relativePath));
        return new DownloadFile(
            sources.OrderSources([primaryUrl], [sources.GetModMirrorSource(primaryUrl)]),
            localPath,
            new DownloadFileCheck(ActualSize: GetLong(file, "fileSize"), Hash: string.IsNullOrWhiteSpace(sha1) ? null : sha1),
            SimulateBrowserHeaders: true);
    }

    private async Task<DownloadFile?> CreateCurseForgePackDownloadFileAsync(
        JsonElement manifestFile,
        string instancePath,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (manifestFile.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        if (client is null)
        {
            warnings.Add("缺少 CurseForge 文件查询客户端，无法解析整合包内文件");
            return null;
        }

        var projectId = GetInt(manifestFile, "projectID", GetInt(manifestFile, "projectId"));
        var fileId = GetInt(manifestFile, "fileID", GetInt(manifestFile, "fileId"));
        if (projectId <= 0 || fileId <= 0)
        {
            warnings.Add("跳过缺少 projectID/fileID 的 CurseForge 文件项");
            return null;
        }

        var url = CommunityResourceVersionService.BuildCurseForgeFileUrl(projectId.ToString(), fileId.ToString());
        var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"CurseForge 文件 {projectId}/{fileId} 返回内容无效");
            return null;
        }

        var fileName = GetSafeFileName(GetString(data, "fileName", fileId + ".jar"));
        var downloadUrl = GetString(data, "downloadUrl");
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            downloadUrl = BuildCurseForgeFallbackDownloadUrl(fileId, fileName);
        }

        string? sha1 = null;
        if (data.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
        {
            foreach (var hash in hashes.EnumerateArray())
            {
                if (GetInt(hash, "algo") == 1)
                {
                    sha1 = GetString(hash, "value");
                    break;
                }
            }
        }

        return new DownloadFile(
            sources.GetModFileSources(downloadUrl),
            Path.Combine(instancePath, "mods", fileName),
            new DownloadFileCheck(ActualSize: GetLong(data, "fileLength"), Hash: string.IsNullOrWhiteSpace(sha1) ? null : sha1),
            SimulateBrowserHeaders: true);
    }

    private static bool ShouldInstallOnClient(JsonElement file)
    {
        if (!file.TryGetProperty("env", out var env) || env.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        var client = GetString(env, "client");
        return !string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private static int ExtractOverrides(ZipArchive archive, string instancePath, string overridesFolder = "overrides")
    {
        var count = 0;
        var prefix = NormalizeEntryPrefix(overridesFolder);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var relative = entry.FullName[prefix.Length..];
            if (!IsSafeRelativePath(relative))
            {
                continue;
            }

            var target = Path.Combine(instancePath, NormalizePackPath(relative));
            var fullTarget = Path.GetFullPath(target);
            var fullRoot = Path.GetFullPath(instancePath);
            if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
            entry.ExtractToFile(fullTarget, overwrite: true);
            count++;
        }

        return count;
    }

    private static void WritePackMetadata(string instancePath, JsonElement root)
    {
        Directory.CreateDirectory(instancePath);
        File.WriteAllText(Path.Combine(instancePath, "modrinth.index.json"), root.GetRawText());
    }

    private static void WriteCurseForgePackMetadata(string instancePath, JsonElement root)
    {
        Directory.CreateDirectory(instancePath);
        File.WriteAllText(Path.Combine(instancePath, "manifest.json"), root.GetRawText());
    }

    private static (string? Name, string? Version) GetLoader(JsonElement dependencies)
    {
        foreach (var key in new[] { "fabric-loader", "quilt-loader", "forge", "neoforge" })
        {
            var version = GetString(dependencies, key);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return (key, version);
            }
        }

        if (dependencies.ValueKind == JsonValueKind.Object)
        {
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (string.Equals(dependency.Name, "minecraft", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dependency.Name, "java", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var version = dependency.Value.ValueKind == JsonValueKind.String
                    ? dependency.Value.GetString()
                    : null;
                return (dependency.Name, version);
            }
        }

        return (null, null);
    }

    private static (string? Name, string? Version) GetCurseForgeLoader(JsonElement minecraft)
    {
        if (!minecraft.TryGetProperty("modLoaders", out var loaders) || loaders.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var selected = loaders.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .OrderByDescending(item => item.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True)
            .FirstOrDefault();
        var id = GetString(selected, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return (null, null);
        }

        return ParseCurseForgeLoaderId(id);
    }

    private static (string? Name, string? Version) ParseCurseForgeLoaderId(string id)
    {
        foreach (var prefix in new[] { "fabric-", "quilt-", "forge-", "neoforge-" })
        {
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var name = prefix.TrimEnd('-');
                return (name == "fabric" ? "fabric-loader" : name == "quilt" ? "quilt-loader" : name, id[prefix.Length..]);
            }
        }

        return (id, null);
    }

    private static string SanitizeInstanceName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Modpack" : sanitized;
    }

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            return false;
        }

        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.All(part => part != "." && part != "..");
    }

    private static string NormalizePackPath(string value)
    {
        return value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string NormalizeEntryPrefix(string value)
    {
        var prefix = string.IsNullOrWhiteSpace(value) ? "overrides" : value.Replace('\\', '/').Trim('/');
        return prefix + "/";
    }

    private static string GetSafeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "download.jar";
        }

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(ch, '_');
        }

        return safeName;
    }

    private static string BuildCurseForgeFallbackDownloadUrl(int fileId, string fileName)
    {
        var id = fileId.ToString();
        var first = id.Length <= 3 ? "0" : id[..^3];
        var second = id.Length <= 3 ? id.PadLeft(3, '0') : id[^3..];
        return "https://edge.forgecdn.net/files/" + first + "/" + second + "/" + Uri.EscapeDataString(fileName);
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var number)
            ? number
            : -1;
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var number)
            ? number
            : defaultValue;
    }
}
