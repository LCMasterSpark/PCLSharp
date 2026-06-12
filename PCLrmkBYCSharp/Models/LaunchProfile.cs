using System.Diagnostics;

namespace PCLrmkBYCSharp.Models;

public sealed record LaunchProfile(
    MinecraftInstance Instance,
    JavaEntry Java,
    LoginSession Login,
    string Arguments,
    string SanitizedCommandLine,
    ProcessStartInfo ProcessStartInfo,
    IReadOnlyList<string> MissingFiles);
