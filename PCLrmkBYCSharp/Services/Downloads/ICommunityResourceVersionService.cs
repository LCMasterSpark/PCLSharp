using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface ICommunityResourceVersionService
{
    Task<IReadOnlyList<CommunityResourceVersion>> GetVersionsAsync(
        CommunityResourceProject project,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);

    DownloadFile CreateDownloadFile(
        CommunityResourceProject project,
        CommunityResourceVersion version,
        CommunityResourceFile file,
        string minecraftRootPath);

    Task<IReadOnlyList<DownloadFile>> CreateDownloadFilesWithDependenciesAsync(
        CommunityResourceProject project,
        CommunityResourceVersion version,
        CommunityResourceFile file,
        string minecraftRootPath,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);
}
