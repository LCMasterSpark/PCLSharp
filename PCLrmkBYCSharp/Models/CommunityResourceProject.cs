namespace PCLrmkBYCSharp.Models;

public sealed record CommunityResourceProject(
    CommunityResourcePlatform Platform,
    CommunityResourceType Type,
    string Id,
    string Slug,
    string Name,
    string Description,
    string WebsiteUrl,
    string? IconUrl,
    int DownloadCount,
    DateTimeOffset Updated,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<string> Categories)
{
    public string PlatformName => Platform == CommunityResourcePlatform.Modrinth ? "Modrinth" : "CurseForge";

    public string DisplayIcon => string.IsNullOrWhiteSpace(IconUrl) ? "/Resources/Images/Icons/NoIcon.png" : IconUrl;

    public string LoaderSummary => Loaders.Count == 0 ? "未知" : string.Join(" / ", Loaders);

    public string VersionSummary => GameVersions.Count == 0 ? "未知版本" : string.Join(", ", GameVersions.Take(4)) + (GameVersions.Count > 4 ? "..." : "");
}
