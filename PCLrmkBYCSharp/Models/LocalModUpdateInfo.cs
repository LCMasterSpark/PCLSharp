namespace PCLrmkBYCSharp.Models;

public sealed record LocalModUpdateInfo(
    string EnabledFileName,
    bool MatchedOnline,
    string ProjectId,
    string CurrentVersionId,
    string CurrentVersionNumber,
    DateTimeOffset CurrentPublished,
    CommunityResourceVersion? LatestVersion,
    CommunityResourceFile? LatestFile,
    string ChangelogUrl)
{
    public bool HasUpdate => LatestVersion is not null && LatestFile is not null;

    public string Summary
    {
        get
        {
            if (!MatchedOnline)
            {
                return "未匹配到在线资源";
            }

            if (!HasUpdate)
            {
                return "已是最新版本";
            }

            var current = string.IsNullOrWhiteSpace(CurrentVersionNumber) ? "当前版本" : CurrentVersionNumber;
            var latest = string.IsNullOrWhiteSpace(LatestVersion!.VersionNumber) ? LatestFile!.FileName : LatestVersion.VersionNumber;
            return $"可更新：{current} -> {latest}";
        }
    }
}
