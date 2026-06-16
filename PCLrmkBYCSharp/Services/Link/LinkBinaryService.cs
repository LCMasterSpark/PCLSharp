using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Link;

/// <summary>
/// 联机后端（Terracotta / EasyTier）可执行文件的自动下载与校验服务。
/// </summary>
public sealed class LinkBinaryService : ILinkBinaryService
{
    private readonly HttpClient _http;
    private readonly IAppPathService _paths;
    private readonly IAppLoggerService _logger;

    private const string EasyTierReleaseApi = "https://api.github.com/repos/EasyTier/EasyTier/releases/latest";
    private const string TerracottaReleaseApi = "https://api.github.com/repos/EasyTier/Terracotta/releases/latest";

    public LinkBinaryService(IAppPathService paths, IAppLoggerService logger, HttpClient? http = null)
    {
        _paths = paths;
        _logger = logger;
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PlainCraftLauncherSharp/0.6pre");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public LinkBinaryInfo GetInstalledInfo(LinkProviderKind provider)
    {
        var exePath = GetExpectedExecutablePath(provider);
        if (!File.Exists(exePath))
            return new LinkBinaryInfo(provider, "", "", 0, DateTime.MinValue);

        try
        {
            var file = new FileInfo(exePath);
            var version = TryReadFileVersion(exePath) ?? "";
            return new LinkBinaryInfo(provider, exePath, version, file.Length, file.LastWriteTimeUtc);
        }
        catch
        {
            return new LinkBinaryInfo(provider, exePath, "", 0, DateTime.MinValue);
        }
    }

    public async Task<LinkBinaryReleaseInfo> FetchLatestReleaseAsync(LinkProviderKind provider, CancellationToken ct = default)
    {
        var apiUrl = provider switch
        {
            LinkProviderKind.Terracotta => TerracottaReleaseApi,
            LinkProviderKind.EasyTier => EasyTierReleaseApi,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "不支持的联机后端类型。")
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "unknown" : "unknown";
        var version = tagName.TrimStart('v');

        Uri? downloadUrl = null;
        string expectedSha = "";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var suffix = provider == LinkProviderKind.Terracotta ? "terracotta" : "easytier-core";
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Contains(suffix, StringComparison.OrdinalIgnoreCase)
                    && name.Contains("windows", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("debug", StringComparison.OrdinalIgnoreCase))
                {
                    if (asset.TryGetProperty("browser_download_url", out var url))
                        downloadUrl = new Uri(url.GetString()!);
                    break;
                }
            }
        }

        downloadUrl ??= new Uri($"https://github.com/EasyTier/{(provider == LinkProviderKind.Terracotta ? "Terracotta" : "EasyTier")}/releases/download/{tagName}/{(provider == LinkProviderKind.Terracotta ? "terracotta" : "easytier-core")}-x86_64-pc-windows-msvc.exe");

        return new LinkBinaryReleaseInfo(provider, version, downloadUrl, expectedSha);
    }

    public async Task<string> DownloadAsync(LinkProviderKind provider, IProgress<long>? progress = null, LinkBinaryReleaseInfo? release = null, CancellationToken ct = default)
    {
        release ??= await FetchLatestReleaseAsync(provider, ct).ConfigureAwait(false);

        var linkDir = Path.Combine(_paths.AppDataDirectory, "Link");
        Directory.CreateDirectory(linkDir);

        var exeName = provider == LinkProviderKind.Terracotta ? "terracotta.exe" : "easytier-core.exe";
        var localPath = Path.Combine(linkDir, exeName);

        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalLength = response.Content.Headers.ContentLength ?? -1;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(localPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long downloaded = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            progress?.Report(totalLength > 0 ? downloaded * 100 / totalLength : downloaded);
        }

        if (!string.IsNullOrWhiteSpace(release.ExpectedSha256))
        {
            await output.FlushAsync(ct).ConfigureAwait(false);
            output.Position = 0;
            var actualSha = await ComputeSha256Async(output, ct).ConfigureAwait(false);
            if (!string.Equals(actualSha, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"联机后端校验和不匹配：期望 {release.ExpectedSha256}，实际 {actualSha}。");
        }

        await output.DisposeAsync().ConfigureAwait(false);

        if (File.Exists(localPath))
            File.Delete(localPath);
        File.Move(localPath + ".tmp", localPath);

        _logger.Info($"联机后端已下载：{release.Provider} {release.Version} → {localPath}");
        return localPath;
    }

    private string GetExpectedExecutablePath(LinkProviderKind provider)
    {
        var linkDir = Path.Combine(_paths.AppDataDirectory, "Link");
        var exeName = provider == LinkProviderKind.Terracotta ? "terracotta.exe" : "easytier-core.exe";
        return Path.Combine(linkDir, exeName);
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct)
    {
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? TryReadFileVersion(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return info.FileVersion ?? info.ProductVersion;
        }
        catch
        {
            return null;
        }
    }
}
