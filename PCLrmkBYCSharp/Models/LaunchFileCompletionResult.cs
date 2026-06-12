namespace PCLrmkBYCSharp.Models;

public sealed record LaunchFileCompletionResult(
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<DownloadFile> Downloads)
{
    public IReadOnlyList<string> DownloadableMissingFiles
    {
        get
        {
            var downloadPaths = Downloads
                .Select(file => file.LocalPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return MissingFiles
                .Where(downloadPaths.Contains)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<string> UnresolvableMissingFiles
    {
        get
        {
            var downloadPaths = Downloads
                .Select(file => file.LocalPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return MissingFiles
                .Where(path => !downloadPaths.Contains(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
