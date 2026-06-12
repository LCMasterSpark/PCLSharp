namespace PCLrmkBYCSharp.Models;

public sealed record LoginSession(
    LoginType Type,
    string UserName,
    string Uuid,
    string AccessToken,
    string ClientToken,
    string ProfileJson = "",
    string AuthlibInjectorMetadata = "");
