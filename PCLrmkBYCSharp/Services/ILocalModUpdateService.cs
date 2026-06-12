using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface ILocalModUpdateService
{
    Task<IReadOnlyDictionary<string, LocalModUpdateInfo>> CheckModrinthUpdatesAsync(
        IReadOnlyList<LocalModFile> mods,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);
}
