namespace PCLrmkBYCSharp.Models;

public sealed record FeatureModuleSnapshot(
    string Title,
    string Status,
    string Description,
    string NextStep);

public sealed record CrashAnalysisSummary(
    string Status,
    string LatestReportPath,
    DateTimeOffset? LatestReportTime,
    int ReportCount);

public sealed record AccountCenterSummary(
    string Status,
    string CurrentLoginType,
    string CurrentDisplayName,
    int CachedAccountCount);

public sealed record SkinCenterSummary(
    string Status,
    string SkinMode,
    string SkinIdentity,
    bool SlimModel);

public sealed record HomeFeedItem(string Title, string Description, string Category);

public sealed record ExtensionPointInfo(string Name, string Description, string Status);
