namespace PCLrmkBYCSharp.Models;

public sealed record LoaderProcessorRunResult(
    bool Success,
    IReadOnlyList<string> ExecutedProcessors,
    IReadOnlyList<string> SkippedProcessors,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> MissingOutputs,
    IReadOnlyList<string> FailedProcessors)
{
    public static LoaderProcessorRunResult EmptySuccess { get; } = new(true, [], [], [], [], []);
}
