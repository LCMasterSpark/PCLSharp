namespace PCLrmkBYCSharp.Models;

public sealed record MicrosoftAccountCacheEntry(
    string Uuid,
    string Name,
    string RefreshToken,
    string AccessToken,
    long ExpiresAt,
    string ProfileJson,
    DateTimeOffset LastUsedAt);
