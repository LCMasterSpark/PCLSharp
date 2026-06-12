using System.Diagnostics;

namespace PCLrmkBYCSharp.Models;

public sealed record LaunchResult(
    bool Success,
    LaunchProfile? Profile,
    IReadOnlyList<LaunchValidationIssue> Issues,
    Process? Process)
{
    public static LaunchResult Failed(params LaunchValidationIssue[] issues) => new(false, null, issues, null);
}
