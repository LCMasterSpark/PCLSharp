using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IHelpService
{
    Task<IReadOnlyList<HelpEntry>> LoadAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<HelpEntry> Search(IReadOnlyList<HelpEntry> entries, string query, int maxCount = 30);
}
