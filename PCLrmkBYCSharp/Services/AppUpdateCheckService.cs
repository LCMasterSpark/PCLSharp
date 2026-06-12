using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.Services;

public sealed class AppUpdateCheckService : IAppUpdateCheckService
{
    public const string DefaultReleaseApiUrl = "https://api.github.com/repos/LCMasterSpark/PlainCraftLauncherSharp/releases/latest";

    private readonly ILaunchHttpClient _http;
    private readonly string _currentVersion;
    private readonly string _defaultSourceUrl;

    public AppUpdateCheckService(
        ILaunchHttpClient? http = null,
        string? currentVersion = null,
        string? defaultSourceUrl = null)
    {
        _http = http ?? new LaunchHttpClient();
        _currentVersion = string.IsNullOrWhiteSpace(currentVersion)
            ? GetAssemblyVersion()
            : currentVersion.Trim();
        _defaultSourceUrl = string.IsNullOrWhiteSpace(defaultSourceUrl)
            ? DefaultReleaseApiUrl
            : defaultSourceUrl.Trim();
    }

    public async Task<AppUpdateInfo> CheckAsync(string? sourceUrl = null, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeSourceUrl(sourceUrl);
        var text = await _http.SendAsync(new LaunchHttpRequest(
            endpoint,
            HttpMethod.Get,
            Headers: new Dictionary<string, string>
            {
                ["Accept"] = "application/vnd.github+json",
                ["User-Agent"] = "PlainCraftLauncherSharp"
            }), cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var latestVersion = ReadString(root, "tag_name");
        var latestName = ReadString(root, "name");
        var releaseUrl = ReadString(root, "html_url");
        var publishedAt = ReadDateTimeOffset(root, "published_at");
        var isUpdateAvailable = IsVersionNewer(latestVersion, _currentVersion);
        return new AppUpdateInfo(
            _currentVersion,
            latestVersion,
            latestName,
            releaseUrl,
            publishedAt,
            isUpdateAvailable,
            endpoint);
    }

    public static bool IsVersionNewer(string latestVersion, string currentVersion)
    {
        var latest = ParseVersion(latestVersion);
        var current = ParseVersion(currentVersion);
        var max = Math.Max(latest.Numbers.Count, current.Numbers.Count);
        for (var i = 0; i < max; i++)
        {
            var left = i < latest.Numbers.Count ? latest.Numbers[i] : 0;
            var right = i < current.Numbers.Count ? current.Numbers[i] : 0;
            if (left != right)
            {
                return left > right;
            }
        }

        if (latest.IsPrerelease != current.IsPrerelease)
        {
            return !latest.IsPrerelease && current.IsPrerelease;
        }

        return false;
    }

    private string NormalizeSourceUrl(string? sourceUrl)
    {
        var value = string.IsNullOrWhiteSpace(sourceUrl) ? _defaultSourceUrl : sourceUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            return uri.ToString();
        }

        return _defaultSourceUrl;
    }

    private static ParsedVersion ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var numbers = new List<int>();
        foreach (var token in normalized.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(token.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                continue;
            }

            numbers.Add(int.Parse(digits));
        }

        var isPrerelease = normalized.Contains("pre", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("alpha", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("beta", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("rc", StringComparison.OrdinalIgnoreCase);
        return new ParsedVersion(numbers, isPrerelease);
    }

    private static string GetAssemblyVersion()
    {
        var assembly = typeof(AppUpdateCheckService).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "未知";
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var result) ? result : null;
    }

    private sealed record ParsedVersion(IReadOnlyList<int> Numbers, bool IsPrerelease);
}
