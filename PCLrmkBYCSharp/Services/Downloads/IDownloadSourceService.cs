namespace PCLrmkBYCSharp.Services.Downloads;

public interface IDownloadSourceService
{
    bool PreferOfficialDownloadsWhenAuto { get; }

    IReadOnlyList<string> OrderSources(IEnumerable<string> officialUrls, IEnumerable<string> mirrorUrls);

    IReadOnlyList<string> GetLauncherOrMetaSources(string original);

    IReadOnlyList<string> GetLibrarySources(string original);

    IReadOnlyList<string> GetAssetSources(string original);

    string GetModMirrorSource(string original);

    IReadOnlyList<string> GetModFileSources(string original);

    void ReportOfficialVersionListLatency(TimeSpan elapsed);
}
