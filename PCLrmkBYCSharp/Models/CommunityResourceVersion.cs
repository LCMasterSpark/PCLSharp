namespace PCLrmkBYCSharp.Models;

public sealed record CommunityResourceVersion(
    CommunityResourcePlatform Platform,
    CommunityResourceType Type,
    string ProjectId,
    string VersionId,
    string Name,
    string VersionNumber,
    DateTimeOffset Published,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<CommunityResourceFile> Files,
    IReadOnlyList<CommunityResourceDependency> Dependencies)
{
    public CommunityResourceFile? PrimaryFile => Files.FirstOrDefault(file => file.IsPrimary) ?? Files.FirstOrDefault();

    public int RequiredDependencyCount => Dependencies.Count(dependency => dependency.IsRequired);

    public string VersionSummary
    {
        get
        {
            var versions = GameVersions.Count == 0 ? "\u672a\u77e5\u7248\u672c" : string.Join(", ", GameVersions.Take(3));
            var loaders = Loaders.Count == 0 ? "" : " / " + string.Join(", ", Loaders.Take(3));
            var dependencies = RequiredDependencyCount == 0 ? "" : $" / \u5fc5\u9700\u4f9d\u8d56 {RequiredDependencyCount}";
            return versions + loaders + dependencies;
        }
    }
}
