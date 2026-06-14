namespace PCLrmkBYCSharp.Services.Launch;

public sealed record GameProcessWatchResult(
    bool IsStillRunning,
    int? ExitCode,
    IReadOnlyList<string> OutputTail,
    IReadOnlyList<string> ErrorTail,
    bool IsEarlyExit)
{
    public bool HasExited => ExitCode.HasValue;

    public static GameProcessWatchResult Running(IReadOnlyList<string>? outputTail = null, IReadOnlyList<string>? errorTail = null)
    {
        return new GameProcessWatchResult(true, null, outputTail ?? [], errorTail ?? [], false);
    }

    public static GameProcessWatchResult Exited(int exitCode, IReadOnlyList<string>? outputTail = null, IReadOnlyList<string>? errorTail = null, bool isEarlyExit = true)
    {
        return new GameProcessWatchResult(false, exitCode, outputTail ?? [], errorTail ?? [], isEarlyExit);
    }

    public IReadOnlyList<string> CombinedTail => OutputTail.Concat(ErrorTail).ToArray();
}
