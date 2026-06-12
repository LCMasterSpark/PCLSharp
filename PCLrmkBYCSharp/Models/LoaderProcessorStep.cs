namespace PCLrmkBYCSharp.Models;

public sealed record LoaderProcessorStep(
    string JarCoordinate,
    IReadOnlyList<string> ClasspathCoordinates,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Outputs,
    bool RunsOnClient);
