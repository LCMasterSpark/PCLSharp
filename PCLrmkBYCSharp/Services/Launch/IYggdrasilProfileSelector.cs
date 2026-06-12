namespace PCLrmkBYCSharp.Services.Launch;

public interface IYggdrasilProfileSelector
{
    Task<YggdrasilProfileOption?> SelectAsync(
        string title,
        IReadOnlyList<YggdrasilProfileOption> profiles,
        string cachedProfileName,
        CancellationToken cancellationToken = default);
}
