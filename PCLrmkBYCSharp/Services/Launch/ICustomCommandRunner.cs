namespace PCLrmkBYCSharp.Services.Launch;

public interface ICustomCommandRunner
{
    Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken = default);
}
