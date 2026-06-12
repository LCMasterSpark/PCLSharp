using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface ILoaderVersionService
{
    Task<IReadOnlyList<LoaderVersionOption>> GetVersionsAsync(
        string loaderKind,
        string minecraftVersion,
        CancellationToken cancellationToken = default);
}
