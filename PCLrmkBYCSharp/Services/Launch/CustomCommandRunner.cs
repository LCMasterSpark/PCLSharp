using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class CustomCommandRunner : ICustomCommandRunner
{
    public async Task RunAsync(string command, string workingDirectory, bool waitForExit, CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + command.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process is not null && waitForExit)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
