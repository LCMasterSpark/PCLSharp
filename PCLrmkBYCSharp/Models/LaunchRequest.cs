namespace PCLrmkBYCSharp.Models;

public sealed record LaunchRequest(
    MinecraftInstance? Instance,
    string MinecraftRootPath,
    string? JavaPath,
    string LegacyName,
    int MinMemoryMb,
    int MaxMemoryMb,
    int WindowWidth,
    int WindowHeight,
    string ExtraJvmArgs,
    string ExtraGameArgs,
    bool StartProcess,
    LoginType LoginType = LoginType.Legacy,
    string ServerIp = "",
    string SaveBatchPath = "",
    int LauncherVisibility = 5,
    int WindowType = 1,
    string LoginUserName = "",
    string LoginPassword = "",
    string LoginServer = "",
    bool RememberLogin = true,
    string GameDirectory = "");
