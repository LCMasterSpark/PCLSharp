using System.IO;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class CommunityResourceVersionService(
    IDownloadByteClient client,
    IDownloadSourceService sources,
    IAppLoggerService logger,
    IAppSettingsService? settings = null) : ICommunityResourceVersionService
{
    private const int MaxDependencyDepth = 8;

    public async Task<IReadOnlyList<CommunityResourceVersion>> GetVersionsAsync(
        CommunityResourceProject project,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        if (project.Platform == CommunityResourcePlatform.CurseForge)
        {
            var curseForgeUrl = BuildCurseForgeVersionsUrl(project.Id, project.Type, gameVersion, loader);
            logger.Info("开始获取 CurseForge 版本：" + curseForgeUrl);
            var curseForgeBytes = await client.GetBytesAsync(curseForgeUrl, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
            using var curseForgeDocument = JsonDocument.Parse(curseForgeBytes);
            if (!curseForgeDocument.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return data
                .EnumerateArray()
                .Select(file => ParseCurseForgeVersion(project, file))
                .Where(version => version.Files.Count > 0)
                .OrderByDescending(version => version.Published)
                .ThenBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var url = BuildModrinthVersionsUrl(project.Id, project.Type, gameVersion, loader);
        logger.Info("\u5f00\u59cb\u83b7\u53d6 Modrinth \u7248\u672c\uff1a" + url);
        var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return document.RootElement
            .EnumerateArray()
            .Select(version => ParseModrinthVersion(project, version))
            .Where(version => version.Files.Count > 0)
            .OrderByDescending(version => version.Published)
            .ThenBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DownloadFile CreateDownloadFile(
        CommunityResourceProject project,
        CommunityResourceVersion version,
        CommunityResourceFile file,
        string minecraftRootPath)
    {
        if (string.IsNullOrWhiteSpace(minecraftRootPath))
        {
            throw new ArgumentException("Minecraft \u6839\u76ee\u5f55\u4e0d\u80fd\u4e3a\u7a7a", nameof(minecraftRootPath));
        }

        if (string.IsNullOrWhiteSpace(file.Url))
        {
            throw new InvalidOperationException("\u8d44\u6e90\u6587\u4ef6\u7f3a\u5c11\u4e0b\u8f7d\u5730\u5740");
        }

        var targetDirectory = project.Type switch
        {
            CommunityResourceType.Mod => Path.Combine(minecraftRootPath, "mods"),
            CommunityResourceType.ResourcePack => Path.Combine(minecraftRootPath, "resourcepacks"),
            CommunityResourceType.Shader => Path.Combine(minecraftRootPath, "shaderpacks"),
            CommunityResourceType.DataPack => Path.Combine(minecraftRootPath, "datapacks"),
            CommunityResourceType.ModPack => Path.Combine(minecraftRootPath, "PCL", "Downloads", "ModPacks"),
            _ => Path.Combine(minecraftRootPath, "PCL", "Downloads")
        };

        var hash = string.IsNullOrWhiteSpace(file.Sha1) ? null : file.Sha1;
        var check = new DownloadFileCheck(ActualSize: file.Size, Hash: hash);
        return new DownloadFile(
            sources.GetModFileSources(file.Url),
            Path.Combine(targetDirectory, GetCommunityResourceFileName(project, file)),
            check,
            SimulateBrowserHeaders: true);
    }

    public async Task<IReadOnlyList<DownloadFile>> CreateDownloadFilesWithDependenciesAsync(
        CommunityResourceProject project,
        CommunityResourceVersion version,
        CommunityResourceFile file,
        string minecraftRootPath,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        var result = new List<DownloadFile>();
        var seenVersionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDownloadFile(result, seenPaths, CreateDownloadFile(project, version, file, minecraftRootPath));
        if (!string.IsNullOrWhiteSpace(version.VersionId))
        {
            seenVersionIds.Add(version.VersionId);
        }

        foreach (var dependency in version.Dependencies.Where(item => item.IsRequired))
        {
            await AddDependencyAsync(project, dependency, minecraftRootPath, gameVersion, loader, result, seenVersionIds, seenPaths, 0, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public static string BuildModrinthVersionsUrl(string projectId, CommunityResourceType type, string gameVersion, string loader)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("\u9879\u76ee ID \u4e0d\u80fd\u4e3a\u7a7a", nameof(projectId));
        }

        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            parameters.Add("game_versions=" + EscapeStringArray([gameVersion.Trim()]));
        }

        if (!string.IsNullOrWhiteSpace(loader) && type is CommunityResourceType.Mod or CommunityResourceType.ModPack)
        {
            parameters.Add("loaders=" + EscapeStringArray([loader.Trim().ToLowerInvariant()]));
        }

        var query = parameters.Count == 0 ? "" : "?" + string.Join("&", parameters);
        return "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId.Trim()) + "/version" + query;
    }

    public static string BuildModrinthVersionUrl(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("\u7248\u672c ID \u4e0d\u80fd\u4e3a\u7a7a", nameof(versionId));
        }

        return "https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId.Trim());
    }

    public static string BuildCurseForgeVersionsUrl(string projectId, CommunityResourceType type, string gameVersion, string loader)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("项目 ID 不能为空", nameof(projectId));
        }

        var parameters = new List<string> { "pageSize=50" };
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            parameters.Add("gameVersion=" + Uri.EscapeDataString(gameVersion.Trim()));
        }

        var loaderType = ToCurseForgeModLoaderType(loader);
        if (loaderType > 0 && type is CommunityResourceType.Mod or CommunityResourceType.ModPack)
        {
            parameters.Add("modLoaderType=" + loaderType);
        }

        return "https://api.curseforge.com/v1/mods/" + Uri.EscapeDataString(projectId.Trim()) + "/files?" + string.Join("&", parameters);
    }

    public static string BuildCurseForgeFileUrl(string projectId, string fileId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("项目 ID 不能为空", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new ArgumentException("文件 ID 不能为空", nameof(fileId));
        }

        return "https://api.curseforge.com/v1/mods/" + Uri.EscapeDataString(projectId.Trim()) + "/files/" + Uri.EscapeDataString(fileId.Trim());
    }

    private async Task AddDependencyAsync(
        CommunityResourceProject parentProject,
        CommunityResourceDependency dependency,
        string minecraftRootPath,
        string gameVersion,
        string loader,
        List<DownloadFile> result,
        HashSet<string> seenVersionIds,
        HashSet<string> seenPaths,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth >= MaxDependencyDepth)
        {
            logger.Warn("\u8d44\u6e90\u4f9d\u8d56\u5c42\u7ea7\u8fc7\u6df1\uff0c\u5df2\u505c\u6b62\u7ee7\u7eed\u89e3\u6790");
            return;
        }

        var dependencyVersion = await ResolveDependencyVersionAsync(parentProject, dependency, gameVersion, loader, cancellationToken)
            .ConfigureAwait(false);
        if (dependencyVersion is null || !seenVersionIds.Add(dependencyVersion.VersionId))
        {
            return;
        }

        var primaryFile = dependencyVersion.PrimaryFile;
        if (primaryFile is null)
        {
            return;
        }

        var dependencyProject = CreateDependencyProject(parentProject, dependencyVersion);
        AddDownloadFile(result, seenPaths, CreateDownloadFile(dependencyProject, dependencyVersion, primaryFile, minecraftRootPath));

        foreach (var nextDependency in dependencyVersion.Dependencies.Where(item => item.IsRequired))
        {
            await AddDependencyAsync(parentProject, nextDependency, minecraftRootPath, gameVersion, loader, result, seenVersionIds, seenPaths, depth + 1, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<CommunityResourceVersion?> ResolveDependencyVersionAsync(
        CommunityResourceProject parentProject,
        CommunityResourceDependency dependency,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(dependency.VersionId))
        {
            if (parentProject.Platform == CommunityResourcePlatform.CurseForge)
            {
                return string.IsNullOrWhiteSpace(dependency.ProjectId)
                    ? null
                    : await GetCurseForgeFileByIdAsync(parentProject, dependency.ProjectId, dependency.VersionId, cancellationToken).ConfigureAwait(false);
            }

            return await GetVersionByIdAsync(parentProject, dependency.VersionId, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(dependency.ProjectId))
        {
            return null;
        }

        var dependencyProject = parentProject with
        {
            Id = dependency.ProjectId,
            Slug = dependency.ProjectId,
            Name = dependency.ProjectId,
            WebsiteUrl = parentProject.Platform == CommunityResourcePlatform.CurseForge
                ? "https://www.curseforge.com/minecraft/mc-mods/" + dependency.ProjectId
                : "https://modrinth.com/project/" + dependency.ProjectId
        };
        return (await GetVersionsAsync(dependencyProject, gameVersion, loader, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<CommunityResourceVersion?> GetVersionByIdAsync(
        CommunityResourceProject parentProject,
        string versionId,
        CancellationToken cancellationToken)
    {
        var url = BuildModrinthVersionUrl(versionId);
        logger.Info("\u5f00\u59cb\u83b7\u53d6 Modrinth \u4f9d\u8d56\u7248\u672c\uff1a" + url);
        var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseModrinthVersion(parentProject, document.RootElement);
    }

    private async Task<CommunityResourceVersion?> GetCurseForgeFileByIdAsync(
        CommunityResourceProject parentProject,
        string projectId,
        string fileId,
        CancellationToken cancellationToken)
    {
        var url = BuildCurseForgeFileUrl(projectId, fileId);
        logger.Info("开始获取 CurseForge 依赖版本：" + url);
        var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dependencyProject = parentProject with
        {
            Id = projectId,
            Slug = projectId,
            Name = projectId,
            WebsiteUrl = "https://www.curseforge.com/minecraft/mc-mods/" + projectId
        };
        return ParseCurseForgeVersion(dependencyProject, data);
    }

    private static void AddDownloadFile(List<DownloadFile> result, HashSet<string> seenPaths, DownloadFile file)
    {
        if (seenPaths.Add(file.LocalPath))
        {
            result.Add(file);
        }
    }

    private static CommunityResourceProject CreateDependencyProject(CommunityResourceProject parentProject, CommunityResourceVersion version)
    {
        var projectId = string.IsNullOrWhiteSpace(version.ProjectId) ? parentProject.Id : version.ProjectId;
        return parentProject with
        {
            Id = projectId,
            Slug = projectId,
            Name = string.IsNullOrWhiteSpace(version.Name) ? projectId : version.Name,
            WebsiteUrl = parentProject.Platform == CommunityResourcePlatform.CurseForge
                ? "https://www.curseforge.com/minecraft/mc-mods/" + projectId
                : "https://modrinth.com/project/" + projectId
        };
    }

    private CommunityResourceVersion ParseModrinthVersion(CommunityResourceProject project, JsonElement element)
    {
        var files = new List<CommunityResourceFile>();
        if (element.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                files.Add(ParseModrinthFile(file));
            }
        }

        var dependencies = new List<CommunityResourceDependency>();
        if (element.TryGetProperty("dependencies", out var dependencyArray) && dependencyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var dependency in dependencyArray.EnumerateArray())
            {
                dependencies.Add(ParseModrinthDependency(dependency));
            }
        }

        return new CommunityResourceVersion(
            project.Platform,
            project.Type,
            GetString(element, "project_id", project.Id),
            GetString(element, "id"),
            GetString(element, "name"),
            GetString(element, "version_number"),
            DateTimeOffset.TryParse(GetString(element, "date_published"), out var published) ? published : DateTimeOffset.MinValue,
            ReadStringArray(element, "game_versions"),
            FilterDisplayedLoaders(ReadStringArray(element, "loaders")),
            files,
            dependencies);
    }

    private CommunityResourceVersion ParseCurseForgeVersion(CommunityResourceProject project, JsonElement element)
    {
        var file = ParseCurseForgeFile(element);
        var gameVersions = ReadCurseForgeGameVersions(element);
        var loaders = FilterDisplayedLoaders(ReadCurseForgeLoaders(element));
        var dependencies = new List<CommunityResourceDependency>();
        if (element.TryGetProperty("dependencies", out var dependencyArray) && dependencyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var dependency in dependencyArray.EnumerateArray())
            {
                dependencies.Add(ParseCurseForgeDependency(dependency));
            }
        }

        var projectId = GetInt(element, "modId");
        return new CommunityResourceVersion(
            CommunityResourcePlatform.CurseForge,
            project.Type,
            projectId > 0 ? projectId.ToString() : project.Id,
            GetInt(element, "id").ToString(),
            GetString(element, "displayName", file.FileName),
            GetString(element, "displayName", file.FileName),
            DateTimeOffset.TryParse(GetString(element, "fileDate"), out var published) ? published : DateTimeOffset.MinValue,
            gameVersions,
            loaders,
            string.IsNullOrWhiteSpace(file.Url) ? [] : [file],
            dependencies);
    }

    private IReadOnlyList<string> FilterDisplayedLoaders(IReadOnlyList<string> loaders)
    {
        if (settings?.Get(AppSettingKeys.ToolDownloadIgnoreQuilt, false) != true)
        {
            return loaders;
        }

        return loaders
            .Where(loader => !string.Equals(loader, "quilt", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string GetSafeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "download.bin";
        }

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
        {
            safeName = safeName.Replace(ch, '_');
        }

        return safeName;
    }

    private string GetCommunityResourceFileName(CommunityResourceProject project, CommunityResourceFile file)
    {
        var fileName = file.FileName;
        if (!string.IsNullOrWhiteSpace(project.Name) && ContainsNonAscii(project.Name))
        {
            var translatedName = GetSafeTranslatedName(project.Name);
            if (!string.IsNullOrWhiteSpace(translatedName)
                && !fileName.Contains(translatedName, StringComparison.OrdinalIgnoreCase))
            {
                fileName = (settings?.Get(AppSettingKeys.ToolDownloadTranslateV2, 1) ?? 1) switch
                {
                    0 => $"【{translatedName}】{file.FileName}",
                    1 => $"[{translatedName}] {file.FileName}",
                    2 => $"{translatedName}-{file.FileName}",
                    3 => $"{file.FileName}-{translatedName}",
                    _ => file.FileName
                };
            }
        }

        if (project.Type == CommunityResourceType.Mod)
        {
            fileName = fileName.Replace("~", "-", StringComparison.Ordinal);
        }

        return GetSafeFileName(fileName);
    }

    private static bool ContainsNonAscii(string value)
    {
        return value.Any(ch => ch > 127);
    }

    private static string GetSafeTranslatedName(string name)
    {
        var safeName = name
            .Split(" (", StringSplitOptions.None)[0]
            .Split(" - ", StringSplitOptions.None)[0]
            .Replace("\\", "＼", StringComparison.Ordinal)
            .Replace("/", "／", StringComparison.Ordinal)
            .Replace("|", "｜", StringComparison.Ordinal)
            .Replace(":", "：", StringComparison.Ordinal)
            .Replace("<", "＜", StringComparison.Ordinal)
            .Replace(">", "＞", StringComparison.Ordinal)
            .Replace("*", "＊", StringComparison.Ordinal)
            .Replace("?", "？", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("： ", "：", StringComparison.Ordinal)
            .Trim();
        return safeName;
    }

    private static CommunityResourceFile ParseModrinthFile(JsonElement element)
    {
        string? sha1 = null;
        string? sha512 = null;
        if (element.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object)
        {
            sha1 = GetStringOrNull(hashes, "sha1");
            sha512 = GetStringOrNull(hashes, "sha512");
        }

        return new CommunityResourceFile(
            GetString(element, "filename"),
            GetString(element, "url"),
            GetLong(element, "size"),
            sha1,
            sha512,
            element.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True);
    }

    private static CommunityResourceFile ParseCurseForgeFile(JsonElement element)
    {
        var fileName = GetString(element, "fileName", "download.jar");
        var url = GetString(element, "downloadUrl");
        var fileId = GetInt(element, "id");
        if (string.IsNullOrWhiteSpace(url) && fileId > 0)
        {
            url = BuildCurseForgeFallbackDownloadUrl(fileId, fileName);
        }

        string? sha1 = null;
        if (element.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
        {
            foreach (var hash in hashes.EnumerateArray())
            {
                if (GetInt(hash, "algo") == 1)
                {
                    sha1 = GetStringOrNull(hash, "value");
                    break;
                }
            }
        }

        return new CommunityResourceFile(
            fileName,
            url,
            GetLong(element, "fileLength"),
            sha1,
            null,
            true);
    }

    private static CommunityResourceDependency ParseModrinthDependency(JsonElement element)
    {
        return new CommunityResourceDependency(
            GetStringOrNull(element, "project_id"),
            GetStringOrNull(element, "version_id"),
            GetString(element, "dependency_type"));
    }

    private static CommunityResourceDependency ParseCurseForgeDependency(JsonElement element)
    {
        var modId = GetInt(element, "modId");
        var fileId = GetInt(element, "fileId");
        return new CommunityResourceDependency(
            modId <= 0 ? null : modId.ToString(),
            fileId <= 0 ? null : fileId.ToString(),
            GetInt(element, "relationType") == 3 ? "required" : "optional");
    }

    private static IReadOnlyList<string> ReadCurseForgeGameVersions(JsonElement element)
    {
        return ReadStringArray(element, "gameVersions")
            .Where(value => !IsCurseForgeLoaderName(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadCurseForgeLoaders(JsonElement element)
    {
        var loaders = ReadStringArray(element, "gameVersions")
            .Where(IsCurseForgeLoaderName)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (loaders.Count > 0)
        {
            return loaders;
        }

        var loaderType = GetInt(element, "modLoader");
        var loader = loaderType <= 0 ? "" : FromCurseForgeModLoaderType(loaderType);
        return string.IsNullOrWhiteSpace(loader) ? [] : [loader];
    }

    private static bool IsCurseForgeLoaderName(string value)
    {
        return value.Equals("forge", StringComparison.OrdinalIgnoreCase)
            || value.Equals("fabric", StringComparison.OrdinalIgnoreCase)
            || value.Equals("quilt", StringComparison.OrdinalIgnoreCase)
            || value.Equals("neoforge", StringComparison.OrdinalIgnoreCase);
    }

    private static int ToCurseForgeModLoaderType(string loader)
    {
        return loader.Trim().ToLowerInvariant() switch
        {
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            "neoforge" => 6,
            _ => 0
        };
    }

    private static string FromCurseForgeModLoaderType(int loaderType)
    {
        return loaderType switch
        {
            1 => "forge",
            4 => "fabric",
            5 => "quilt",
            6 => "neoforge",
            _ => ""
        };
    }

    private static string BuildCurseForgeFallbackDownloadUrl(int fileId, string fileName)
    {
        var id = fileId.ToString();
        var first = id.Length <= 3 ? "0" : id[..^3];
        var second = id.Length <= 3 ? id.PadLeft(3, '0') : id[^3..];
        return "https://edge.forgecdn.net/files/" + first + "/" + second + "/" + Uri.EscapeDataString(fileName);
    }

    private static string EscapeStringArray(IReadOnlyList<string> values)
    {
        return Uri.EscapeDataString(JsonSerializer.Serialize(values));
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number) ? number : 0;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : 0;
    }
}
