using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IHelpActionService
{
    void SetEventHandler(string eventType, Func<string, CancellationToken, Task<HelpActionResult>> handler);

    Task<HelpActionResult> ExecuteAsync(HelpEntry entry, CancellationToken cancellationToken = default);
}
