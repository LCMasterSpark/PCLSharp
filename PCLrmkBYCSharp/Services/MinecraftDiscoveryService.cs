using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed partial class MinecraftDiscoveryService : IMinecraftDiscoveryService
{
    private readonly IMinecraftInstanceManagementService _instanceManagement;

    private static readonly MinecraftVersionInfo EmptyVersion = new(
        "",
        "",
        null,
        null,
        "",
        "",
        "",
        false,
        false,
        false,
        false);

    public MinecraftDiscoveryService()
        : this(new MinecraftInstanceManagementService())
    {
    }

    public MinecraftDiscoveryService(IMinecraftInstanceManagementService instanceManagement)
    {
        _instanceManagement = instanceManagement;
    }

    public string GetDefaultMinecraftRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");
    }

    public Task<IReadOnlyList<MinecraftInstance>> ScanAsync(string? rootPath, CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var versionsPath = Path.Combine(root, "versions");
        if (!Directory.Exists(versionsPath))
        {
            return Task.FromResult<IReadOnlyList<MinecraftInstance>>([]);
        }

        var versionDirectories = Directory
            .EnumerateDirectories(versionsPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var available = versionDirectories
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var instances = new List<MinecraftInstance>();
        foreach (var versionPath in versionDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            instances.Add(InspectInstance(root, versionPath, available));
        }

        var ordered = instances
            .OrderBy(instance => instance.HasError)
            .ThenByDescending(instance => instance.Version.ReleaseTime ?? DateTimeOffset.MinValue)
            .ThenBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<MinecraftInstance>>(ordered);
    }

    public MinecraftInstance InspectInstance(string rootPath, string versionPath, IReadOnlySet<string> availableInstances)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(versionPath));
        var jsonPath = Path.Combine(versionPath, $"{name}.json");
        if (!File.Exists(jsonPath))
        {
            return CreateInstance(name, rootPath, versionPath, jsonPath, MinecraftInstanceState.MissingJson, EmptyVersion, "缺少版本 JSON 文件");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var info = ParseVersionInfo(name, document.RootElement);
            if (!string.IsNullOrWhiteSpace(info.InheritsFrom) && !availableInstances.Contains(info.InheritsFrom))
            {
                return CreateInstance(name, rootPath, versionPath, jsonPath, MinecraftInstanceState.MissingInherit, info, $"缺少前置版本：{info.InheritsFrom}");
            }

            return CreateInstance(name, rootPath, versionPath, jsonPath, MinecraftInstanceState.Ready, info, "");
        }
        catch (JsonException ex)
        {
            return CreateInstance(name, rootPath, versionPath, jsonPath, MinecraftInstanceState.InvalidJson, EmptyVersion, $"版本 JSON 解析失败：{ex.Message}");
        }
        catch (IOException ex)
        {
            return CreateInstance(name, rootPath, versionPath, jsonPath, MinecraftInstanceState.InvalidJson, EmptyVersion, $"版本 JSON 读取失败：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return CreateInstance(name, rootPath, versionPath, jsonPath, MinecraftInstanceState.InvalidJson, EmptyVersion, $"版本 JSON 无法访问：{ex.Message}");
        }
    }

    private static MinecraftVersionInfo ParseVersionInfo(string folderName, JsonElement root)
    {
        var id = GetString(root, "id");
        var type = GetString(root, "type");
        var inheritsFrom = GetString(root, "inheritsFrom");
        var mainClass = GetString(root, "mainClass");
        var releaseTime = GetDate(root, "releaseTime");
        var time = GetDate(root, "time");
        var rawJson = root.GetRawText();
        var libraryCount = root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array
            ? libraries.GetArrayLength()
            : 0;
        var assetsIndex = GetAssetsIndex(root);
        var hasLegacyMinecraftArguments = !string.IsNullOrWhiteSpace(GetString(root, "minecraftArguments"));
        var hasModernArguments = root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object;

        return new MinecraftVersionInfo(
            string.IsNullOrWhiteSpace(id) ? folderName : id,
            type,
            releaseTime,
            time,
            inheritsFrom,
            mainClass,
            GuessVanillaVersion(folderName, root, rawJson, inheritsFrom),
            rawJson.Contains("net.minecraftforge", StringComparison.OrdinalIgnoreCase),
            rawJson.Contains("net.fabricmc", StringComparison.OrdinalIgnoreCase) || rawJson.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase),
            rawJson.Contains("net.neoforged", StringComparison.OrdinalIgnoreCase) || rawJson.Contains("neoforge", StringComparison.OrdinalIgnoreCase),
            rawJson.Contains("optifine", StringComparison.OrdinalIgnoreCase),
            libraryCount,
            assetsIndex,
            hasLegacyMinecraftArguments,
            hasModernArguments);
    }

    private MinecraftInstance CreateInstance(
        string name,
        string rootPath,
        string versionPath,
        string jsonPath,
        MinecraftInstanceState state,
        MinecraftVersionInfo version,
        string errorMessage)
    {
        var metadata = _instanceManagement.ReadMetadata(versionPath);
        return new MinecraftInstance(
            name,
            NormalizeRoot(rootPath),
            Path.GetFullPath(versionPath),
            Path.GetFullPath(jsonPath),
            state,
            version,
            errorMessage,
            metadata.IsStar,
            metadata.DisplayType,
            metadata.CustomInfo);
    }

    private static string NormalizeRoot(string? rootPath)
    {
        var root = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : Environment.ExpandEnvironmentVariables(rootPath.Trim());
        return Path.GetFullPath(root);
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static DateTimeOffset? GetDate(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string GetAssetsIndex(JsonElement root)
    {
        if (root.TryGetProperty("assetIndex", out var assetIndex) && assetIndex.ValueKind == JsonValueKind.Object)
        {
            var id = GetString(assetIndex, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return GetString(root, "assets");
    }

    private static string GuessVanillaVersion(string folderName, JsonElement root, string rawJson, string inheritsFrom)
    {
        var clientVersion = GetString(root, "clientVersion");
        if (!string.IsNullOrWhiteSpace(clientVersion))
        {
            return clientVersion;
        }

        if (!string.IsNullOrWhiteSpace(inheritsFrom))
        {
            return inheritsFrom;
        }

        var id = GetString(root, "id");
        var match = MinecraftVersionRegex().Match(string.IsNullOrWhiteSpace(id) ? folderName : id);
        if (match.Success)
        {
            return match.Value;
        }

        match = MinecraftVersionRegex().Match(rawJson);
        return match.Success ? match.Value : id;
    }

    [GeneratedRegex(@"(([1-9][0-9]w[0-9]{2}[a-g])|((1|[2-9][0-9])\.[0-9]+(\.[0-9]+)?(-(pre|rc|snapshot-?)[1-9]*| Pre-Release( [1-9])?)?))(_unobfuscated)?", RegexOptions.IgnoreCase)]
    private static partial Regex MinecraftVersionRegex();
}
