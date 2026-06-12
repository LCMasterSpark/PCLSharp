using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Services;

public sealed class LocalModUpdateService(IDownloadByteClient client, IAppLoggerService logger) : ILocalModUpdateService
{
    public async Task<IReadOnlyDictionary<string, LocalModUpdateInfo>> CheckModrinthUpdatesAsync(
        IReadOnlyList<LocalModFile> mods,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, LocalModUpdateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(mod => File.Exists(mod.FilePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = await CheckOneAsync(mod, gameVersion, loader, cancellationToken).ConfigureAwait(false);
                result[mod.EnabledFileName] = info;
            }
            catch (Exception ex)
            {
                logger.Warn("检查本地 Mod 更新失败：" + mod.FileName + "，" + ex.Message);
                result[mod.EnabledFileName] = new LocalModUpdateInfo(mod.EnabledFileName, false, "", "", "", DateTimeOffset.MinValue, null, null, "");
            }
        }

        return result;
    }

    private async Task<LocalModUpdateInfo> CheckOneAsync(LocalModFile mod, string gameVersion, string loader, CancellationToken cancellationToken)
    {
        var sha1 = await ComputeSha1Async(mod.FilePath, cancellationToken).ConfigureAwait(false);
        var currentUrl = "https://api.modrinth.com/v2/version_file/" + Uri.EscapeDataString(sha1) + "?algorithm=sha1";
        logger.Info("开始匹配本地 Modrinth 文件：" + mod.FileName);
        var currentBytes = await client.GetBytesAsync(currentUrl, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var currentDocument = JsonDocument.Parse(currentBytes);
        if (currentDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new LocalModUpdateInfo(mod.EnabledFileName, false, "", "", "", DateTimeOffset.MinValue, null, null, "");
        }

        var current = ParseModrinthVersion(CommunityResourceType.Mod, currentDocument.RootElement);
        if (string.IsNullOrWhiteSpace(current.ProjectId))
        {
            return new LocalModUpdateInfo(mod.EnabledFileName, false, "", "", "", DateTimeOffset.MinValue, null, null, "");
        }

        var versionsUrl = CommunityResourceVersionService.BuildModrinthVersionsUrl(current.ProjectId, CommunityResourceType.Mod, gameVersion, loader);
        logger.Info("开始检查本地 Mod 更新：" + versionsUrl);
        var versionsBytes = await client.GetBytesAsync(versionsUrl, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
        using var versionsDocument = JsonDocument.Parse(versionsBytes);
        if (versionsDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return CreateInfo(mod, current, null, null);
        }

        var latest = versionsDocument.RootElement
            .EnumerateArray()
            .Select(element => ParseModrinthVersion(CommunityResourceType.Mod, element))
            .Where(version => version.Files.Count > 0)
            .OrderByDescending(version => version.Published)
            .FirstOrDefault();
        if (latest is null || latest.Published <= current.Published)
        {
            return CreateInfo(mod, current, null, null);
        }

        var latestFile = latest.PrimaryFile;
        if (latestFile is null || string.Equals(latestFile.Sha1, sha1, StringComparison.OrdinalIgnoreCase))
        {
            return CreateInfo(mod, current, null, null);
        }

        var changelogUrl = "https://modrinth.com/mod/" + current.ProjectId + "/changelog?g=" + Uri.EscapeDataString(gameVersion);
        return CreateInfo(mod, current, latest, latestFile, changelogUrl);
    }

    private static LocalModUpdateInfo CreateInfo(
        LocalModFile mod,
        CommunityResourceVersion current,
        CommunityResourceVersion? latest,
        CommunityResourceFile? latestFile,
        string changelogUrl = "")
    {
        return new LocalModUpdateInfo(
            mod.EnabledFileName,
            true,
            current.ProjectId,
            current.VersionId,
            current.VersionNumber,
            current.Published,
            latest,
            latestFile,
            changelogUrl);
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static CommunityResourceVersion ParseModrinthVersion(CommunityResourceType type, JsonElement element)
    {
        var files = new List<CommunityResourceFile>();
        if (element.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                files.Add(ParseModrinthFile(file));
            }
        }

        return new CommunityResourceVersion(
            CommunityResourcePlatform.Modrinth,
            type,
            GetString(element, "project_id"),
            GetString(element, "id"),
            GetString(element, "name"),
            GetString(element, "version_number"),
            DateTimeOffset.TryParse(GetString(element, "date_published"), out var published) ? published : DateTimeOffset.MinValue,
            ReadStringArray(element, "game_versions"),
            ReadStringArray(element, "loaders"),
            files,
            []);
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
}
