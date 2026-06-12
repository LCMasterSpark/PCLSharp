namespace PCLrmkBYCSharp.Models;

public sealed record LaunchArgumentBuildResult(
    string Arguments,
    string SanitizedCommandLine,
    IReadOnlyList<string> MissingFiles);
