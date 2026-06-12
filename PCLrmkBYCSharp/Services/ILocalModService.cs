using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface ILocalModService
{
    Task<IReadOnlyList<LocalModFile>> ScanAsync(string modsDirectory, CancellationToken cancellationToken = default);

    LocalModFile SetEnabled(LocalModFile mod, bool enabled);

    void Delete(LocalModFile mod);

    IReadOnlyList<LocalModFile> Install(string modsDirectory, IEnumerable<string> sourceFiles);
}
