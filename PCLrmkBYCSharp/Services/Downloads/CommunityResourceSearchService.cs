using System.Net.Http;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class CommunityResourceSearchService(
    IDownloadByteClient client,
    IAppLoggerService logger,
    IAppSettingsService? settings = null) : ICommunityResourceSearchService
{
    public async Task<CommunityResourceSearchResult> SearchAsync(CommunityResourceSearchQuery query, CancellationToken cancellationToken = default)
    {
        var projects = new List<CommunityResourceProject>();
        var totalHits = 0;
        var sourceNames = new List<string>();
        var failures = new List<string>();

        try
        {
            var curseForgeUrl = BuildCurseForgeSearchUrl(query);
            logger.Info("开始 CurseForge 搜索：" + curseForgeUrl);
            var curseForgeBytes = await client.GetBytesAsync(curseForgeUrl, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
            using var curseForgeDocument = JsonDocument.Parse(curseForgeBytes);
            var curseForgeRoot = curseForgeDocument.RootElement;
            if (curseForgeRoot.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    projects.Add(ParseCurseForgeProject(item, query.Type));
                }
            }

            totalHits += curseForgeRoot.TryGetProperty("pagination", out var pagination)
                && pagination.TryGetProperty("totalCount", out var total)
                && total.TryGetInt32(out var totalValue)
                    ? totalValue
                    : projects.Count(project => project.Platform == CommunityResourcePlatform.CurseForge);
            sourceNames.Add("CurseForge");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            failures.Add("CurseForge");
            logger.Warn("CurseForge 搜索失败，继续使用其他来源：" + ex.Message);
        }

        try
        {
            var url = BuildModrinthSearchUrl(query);
            logger.Info("开始 Modrinth 搜索：" + url);
            var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
            {
                foreach (var hit in hits.EnumerateArray())
                {
                    projects.Add(ParseModrinthProject(hit, query.Type));
                }
            }

            totalHits += root.TryGetProperty("total_hits", out var total) && total.TryGetInt32(out var totalValue)
                ? totalValue
                : projects.Count(project => project.Platform == CommunityResourcePlatform.Modrinth);
            sourceNames.Add("Modrinth");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            failures.Add("Modrinth");
            logger.Warn("Modrinth 搜索失败，继续使用其他来源：" + ex.Message);
        }

        var sourceMessage = sourceNames.Count == 0
            ? "社区资源"
            : string.Join(" + ", sourceNames) + (failures.Count == 0 ? "" : "（" + string.Join("、", failures) + " 失败）");
        return new CommunityResourceSearchResult(projects, totalHits, sourceMessage);
    }

    public static string BuildModrinthSearchUrl(CommunityResourceSearchQuery query)
    {
        var parameters = new List<string>
        {
            "limit=" + Math.Clamp(query.Limit, 1, 100),
            "offset=" + Math.Max(0, query.Offset),
            "index=relevance"
        };
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            parameters.Add("query=" + Uri.EscapeDataString(query.SearchText.Trim()));
        }

        var facets = new List<string> { $"[\"project_type:{ToModrinthProjectType(query.Type)}\"]" };
        if (!string.IsNullOrWhiteSpace(query.GameVersion))
        {
            facets.Add($"[\"versions:{EscapeFacet(query.GameVersion)}\"]");
        }

        if (!string.IsNullOrWhiteSpace(query.Loader) && query.Type is CommunityResourceType.Mod or CommunityResourceType.ModPack)
        {
            facets.Add($"[\"categories:{EscapeFacet(query.Loader.ToLowerInvariant())}\"]");
        }

        if (query.Type == CommunityResourceType.DataPack)
        {
            facets.Add("[\"categories:datapack\"]");
        }

        parameters.Add("facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]"));
        return "https://api.modrinth.com/v2/search?" + string.Join("&", parameters);
    }

    public static string BuildCurseForgeSearchUrl(CommunityResourceSearchQuery query)
    {
        var parameters = new List<string>
        {
            "gameId=432",
            "sortField=2",
            "sortOrder=desc",
            "pageSize=" + Math.Clamp(query.Limit, 1, 100),
            "index=" + Math.Max(0, query.Offset),
            "classId=" + ToCurseForgeClassId(query.Type)
        };
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            parameters.Add("searchFilter=" + Uri.EscapeDataString(query.SearchText.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.GameVersion))
        {
            parameters.Add("gameVersion=" + Uri.EscapeDataString(query.GameVersion.Trim()));
        }

        var loaderType = ToCurseForgeModLoaderType(query.Loader);
        if (loaderType > 0 && query.Type is CommunityResourceType.Mod or CommunityResourceType.ModPack)
        {
            parameters.Add("modLoaderType=" + loaderType);
        }

        return "https://api.curseforge.com/v1/mods/search?" + string.Join("&", parameters);
    }

    private CommunityResourceProject ParseModrinthProject(JsonElement hit, CommunityResourceType fallbackType)
    {
        var categories = ReadStringArray(hit, "categories");
        var loaders = ReadStringArray(hit, "loaders");
        var projectType = GetString(hit, "project_type");
        var type = projectType switch
        {
            "modpack" => CommunityResourceType.ModPack,
            "resourcepack" => CommunityResourceType.ResourcePack,
            "shader" => CommunityResourceType.Shader,
            _ => categories.Contains("datapack", StringComparer.OrdinalIgnoreCase) ? CommunityResourceType.DataPack : fallbackType
        };

        return new CommunityResourceProject(
            CommunityResourcePlatform.Modrinth,
            type,
            GetString(hit, "project_id", GetString(hit, "id")),
            GetString(hit, "slug"),
            GetString(hit, "title"),
            GetString(hit, "description"),
            "https://modrinth.com/" + projectType + "/" + GetString(hit, "slug"),
            GetStringOrNull(hit, "icon_url"),
            GetInt(hit, "downloads"),
            DateTimeOffset.TryParse(GetString(hit, "date_modified"), out var updated) ? updated : DateTimeOffset.MinValue,
            ReadStringArray(hit, "versions"),
            FilterDisplayedLoaders(loaders.Count == 0 ? categories.Where(IsLoaderCategory).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : loaders),
            categories);
    }

    private CommunityResourceProject ParseCurseForgeProject(JsonElement item, CommunityResourceType fallbackType)
    {
        var categories = ReadCurseForgeCategories(item);
        var loaders = ReadCurseForgeLoaders(item);
        var gameVersions = ReadCurseForgeGameVersions(item);
        var id = GetInt(item, "id").ToString();
        var slug = GetString(item, "slug", id);
        return new CommunityResourceProject(
            CommunityResourcePlatform.CurseForge,
            FromCurseForgeClassId(GetInt(item, "classId"), fallbackType),
            id,
            slug,
            GetString(item, "name", slug),
            GetString(item, "summary"),
            GetCurseForgeWebsiteUrl(item, slug),
            GetCurseForgeIconUrl(item),
            GetInt(item, "downloadCount"),
            DateTimeOffset.TryParse(GetString(item, "dateModified"), out var updated) ? updated : DateTimeOffset.MinValue,
            gameVersions,
            FilterDisplayedLoaders(loaders),
            categories);
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

    private static string ToModrinthProjectType(CommunityResourceType type)
    {
        return type switch
        {
            CommunityResourceType.ModPack => "modpack",
            CommunityResourceType.ResourcePack => "resourcepack",
            CommunityResourceType.Shader => "shader",
            _ => "mod"
        };
    }

    private static int ToCurseForgeClassId(CommunityResourceType type)
    {
        return type switch
        {
            CommunityResourceType.ModPack => 4471,
            CommunityResourceType.ResourcePack => 12,
            CommunityResourceType.Shader => 6552,
            _ => 6
        };
    }

    private static CommunityResourceType FromCurseForgeClassId(int classId, CommunityResourceType fallbackType)
    {
        return classId switch
        {
            4471 => CommunityResourceType.ModPack,
            12 => CommunityResourceType.ResourcePack,
            6552 => CommunityResourceType.Shader,
            _ => fallbackType
        };
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

    private static bool IsLoaderCategory(string value)
    {
        return value is "forge" or "fabric" or "quilt" or "neoforge";
    }

    private static string EscapeFacet(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
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

    private static IReadOnlyList<string> ReadCurseForgeCategories(JsonElement item)
    {
        if (!item.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return categories.EnumerateArray()
            .Select(category => GetString(category, "slug", GetString(category, "name")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadCurseForgeGameVersions(JsonElement item)
    {
        if (!item.TryGetProperty("latestFilesIndexes", out var indexes) || indexes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return indexes.EnumerateArray()
            .Select(index => GetString(index, "gameVersion"))
            .Where(value => !string.IsNullOrWhiteSpace(value) && !IsLoaderCategory(value.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadCurseForgeLoaders(JsonElement item)
    {
        if (!item.TryGetProperty("latestFilesIndexes", out var indexes) || indexes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return indexes.EnumerateArray()
            .Select(index => GetString(index, "modLoader"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetCurseForgeWebsiteUrl(JsonElement item, string slug)
    {
        if (item.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object)
        {
            var website = GetString(links, "websiteUrl");
            if (!string.IsNullOrWhiteSpace(website))
            {
                return website;
            }
        }

        return "https://www.curseforge.com/minecraft/mc-mods/" + slug;
    }

    private static string? GetCurseForgeIconUrl(JsonElement item)
    {
        if (!item.TryGetProperty("logo", out var logo) || logo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetStringOrNull(logo, "thumbnailUrl") ?? GetStringOrNull(logo, "url");
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

    private static int GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : 0;
    }
}
