using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IAppUpdateCheckService
{
    Task<AppUpdateInfo> CheckAsync(string? sourceUrl = null, CancellationToken cancellationToken = default);
}
