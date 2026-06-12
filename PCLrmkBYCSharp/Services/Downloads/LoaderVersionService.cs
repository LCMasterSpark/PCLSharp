using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class LoaderVersionService(IDownloadByteClient client, IAppLoggerService logger) : ILoaderVersionService
{
    private const string ForgeMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string NeoForgeMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    public async Task<IReadOnlyList<LoaderVersionOption>> GetVersionsAsync(
        string loaderKind,
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(loaderKind))
        {
            return [];
        }

        return loaderKind.Trim().ToLowerInvariant() switch
        {
            "fabric" => await GetFabricVersionsAsync(minecraftVersion, cancellationToken).ConfigureAwait(false),
            "quilt" => await GetQuiltVersionsAsync(minecraftVersion, cancellationToken).ConfigureAwait(false),
            "forge" => await GetForgeVersionsAsync(minecraftVersion, cancellationToken).ConfigureAwait(false),
            "neoforge" => await GetNeoForgeVersionsAsync(cancellationToken).ConfigureAwait(false),
            _ => []
        };
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> GetFabricVersionsAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        var url = "https://meta.fabricmc.net/v2/versions/loader/" + Uri.EscapeDataString(minecraftVersion.Trim());
        try
        {
            var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var result = new List<LoaderVersionOption>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var loader = item.TryGetProperty("loader", out var loaderNode) ? loaderNode : item;
                var version = GetString(loader, "version");
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                result.Add(new LoaderVersionOption("Fabric", version, GetBool(loader, "stable"), "Fabric Meta"));
            }

            return result.DistinctBy(item => item.Version, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn("获取 Fabric 加载器版本失败：" + ex.Message);
            return [];
        }
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> GetQuiltVersionsAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        var url = "https://meta.quiltmc.org/v3/versions/loader/" + Uri.EscapeDataString(minecraftVersion.Trim());
        try
        {
            var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var result = new List<LoaderVersionOption>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var loader = item.TryGetProperty("loader", out var loaderNode) ? loaderNode : item;
                var version = GetString(loader, "version");
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                result.Add(new LoaderVersionOption("Quilt", version, GetBool(loader, "stable"), "Quilt Meta"));
            }

            return result.DistinctBy(item => item.Version, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn("获取 Quilt 加载器版本失败：" + ex.Message);
            return [];
        }
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> GetForgeVersionsAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        var allVersions = await ReadMavenVersionsAsync(ForgeMetadataUrl, cancellationToken).ConfigureAwait(false);
        var prefix = minecraftVersion.Trim() + "-";
        return allVersions
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(version => version[prefix.Length..])
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseVersionForSort)
            .ThenByDescending(version => version, StringComparer.OrdinalIgnoreCase)
            .Select(version => new LoaderVersionOption("Forge", version, true, "Forge Maven"))
            .ToList();
    }

    private async Task<IReadOnlyList<LoaderVersionOption>> GetNeoForgeVersionsAsync(CancellationToken cancellationToken)
    {
        var allVersions = await ReadMavenVersionsAsync(NeoForgeMetadataUrl, cancellationToken).ConfigureAwait(false);
        return allVersions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseVersionForSort)
            .ThenByDescending(version => version, StringComparer.OrdinalIgnoreCase)
            .Select(version => new LoaderVersionOption("NeoForge", version, true, "NeoForge Maven"))
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ReadMavenVersionsAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await client.GetBytesAsync(url, simulateBrowserHeaders: true, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            return document
                .Descendants("version")
                .Select(element => element.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn("获取 Maven 加载器版本失败：" + ex.Message);
            return [];
        }
    }

    private static Version ParseVersionForSort(string version)
    {
        var numeric = new string(version
            .TakeWhile(ch => char.IsDigit(ch) || ch == '.')
            .ToArray());
        return Version.TryParse(numeric, out var parsed) ? parsed : new Version(0, 0);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }
}
