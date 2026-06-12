namespace PCLrmkBYCSharp.Models;

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string LatestName,
    string ReleaseUrl,
    DateTimeOffset? PublishedAt,
    bool IsUpdateAvailable,
    string SourceUrl)
{
    public string Summary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LatestVersion))
            {
                return "未获取到最新版本信息";
            }

            var latest = string.IsNullOrWhiteSpace(LatestName) || string.Equals(LatestName, LatestVersion, StringComparison.OrdinalIgnoreCase)
                ? LatestVersion
                : $"{LatestName} ({LatestVersion})";
            return IsUpdateAvailable
                ? $"发现新版本：{latest}，当前版本：{CurrentVersion}"
                : $"当前已是最新版本：{CurrentVersion}";
        }
    }
}
