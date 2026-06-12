using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IMicrosoftLoginService
{
    Task<LoginSession> LoginAsync(CancellationToken cancellationToken = default, bool forceNewLogin = false);
}
