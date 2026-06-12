using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IYggdrasilLoginService
{
    Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
