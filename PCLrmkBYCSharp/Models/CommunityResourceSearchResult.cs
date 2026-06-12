namespace PCLrmkBYCSharp.Models;

public sealed record CommunityResourceSearchResult(
    IReadOnlyList<CommunityResourceProject> Projects,
    int TotalHits,
    string SourceMessage);
