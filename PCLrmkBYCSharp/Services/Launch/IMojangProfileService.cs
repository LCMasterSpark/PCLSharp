namespace PCLrmkBYCSharp.Services.Launch;

public interface IMojangProfileService
{
    Task<string?> GetUuidAsync(string userName, CancellationToken cancellationToken = default);
}
