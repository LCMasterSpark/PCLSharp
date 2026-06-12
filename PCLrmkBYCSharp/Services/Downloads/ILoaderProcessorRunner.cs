using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface ILoaderProcessorRunner
{
    Task<LoaderProcessorRunResult> RunAsync(
        string minecraftRootPath,
        string javaPath,
        IReadOnlyList<LoaderProcessorStep> processors,
        CancellationToken cancellationToken = default);
}
