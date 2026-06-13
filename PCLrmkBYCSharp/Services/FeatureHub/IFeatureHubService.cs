using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.FeatureHub;

public interface IFeatureHubService
{
    IReadOnlyList<FeatureModuleSnapshot> GetModules();

    IReadOnlyList<HomeFeedItem> GetHomeFeedItems();

    CrashAnalysisSummary AnalyzeCrashes();

    AccountCenterSummary GetAccountSummary();

    SkinCenterSummary GetSkinSummary();

    IReadOnlyList<ExtensionPointInfo> GetExtensionPoints();
}
