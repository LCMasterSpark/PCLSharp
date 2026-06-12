using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface ILoginService
{
    Task<LoginSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
