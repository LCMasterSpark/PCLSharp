namespace PCLrmkBYCSharp.Models;

public sealed record LoginRequest(
    LoginType Type,
    string LegacyName,
    string UserName,
    string Password,
    string Server,
    bool Remember,
    bool ForceNewLogin = false);
