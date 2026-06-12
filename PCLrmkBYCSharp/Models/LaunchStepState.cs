namespace PCLrmkBYCSharp.Models;

public sealed record LaunchStepState(
    string Name,
    LaunchStepStatus Status,
    string Message);
