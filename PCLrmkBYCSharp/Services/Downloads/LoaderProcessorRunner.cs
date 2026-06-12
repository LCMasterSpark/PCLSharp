using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class LoaderProcessorRunner(IProcessLauncher processLauncher, IAppLoggerService logger) : ILoaderProcessorRunner
{
    public async Task<LoaderProcessorRunResult> RunAsync(
        string minecraftRootPath,
        string javaPath,
        IReadOnlyList<LoaderProcessorStep> processors,
        CancellationToken cancellationToken = default)
    {
        if (processors.Count == 0)
        {
            return LoaderProcessorRunResult.EmptySuccess;
        }

        var executed = new List<string>();
        var skipped = new List<string>();
        var missingInputs = new List<string>();
        var missingOutputs = new List<string>();
        var failed = new List<string>();

        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
        {
            missingInputs.Add(string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath);
            return CreateResult(executed, skipped, missingInputs, missingOutputs, failed);
        }

        foreach (var processor in processors.Where(item => item.RunsOnClient))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processorName = processor.JarCoordinate;
            var processorJar = ResolveLibraryPath(minecraftRootPath, processor.JarCoordinate);
            var classpath = processor.ClasspathCoordinates
                .Select(coordinate => ResolveLibraryPath(minecraftRootPath, coordinate))
                .ToList();
            var missingForProcessor = classpath
                .Prepend(processorJar)
                .Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingForProcessor.Count > 0)
            {
                skipped.Add(processorName);
                missingInputs.AddRange(missingForProcessor);
                continue;
            }

            try
            {
                var startInfo = BuildStartInfo(minecraftRootPath, javaPath, processor, processorJar, classpath);
                logger.Info("Running loader processor: " + processorName);
                var process = processLauncher.Start(startInfo);
                var stdout = TryReadToEnd(process, readError: false, cancellationToken);
                var stderr = TryReadToEnd(process, readError: true, cancellationToken);
                var exitCode = await processLauncher.WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);
                await IgnoreReadFailureAsync(stdout).ConfigureAwait(false);
                await IgnoreReadFailureAsync(stderr).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    failed.Add(processorName + " exited with " + exitCode);
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Error(ex, "Loader processor failed: " + processorName);
                failed.Add(processorName + ": " + ex.Message);
                continue;
            }

            var missingProcessorOutputs = processor.Outputs.Values
                .Select(value => ResolveOutputPath(minecraftRootPath, value))
                .Where(path => !string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingProcessorOutputs.Count > 0)
            {
                missingOutputs.AddRange(missingProcessorOutputs);
                failed.Add(processorName + " missing outputs");
                continue;
            }

            executed.Add(processorName);
        }

        return CreateResult(executed, skipped, missingInputs, missingOutputs, failed);
    }

    public static string ResolveLibraryPath(string minecraftRootPath, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = UnwrapBracketedMavenCoordinate(value);
        if (Path.IsPathRooted(value))
        {
            return value;
        }

        var mavenPath = TryCreateMavenPath(value);
        if (!string.IsNullOrWhiteSpace(mavenPath))
        {
            return Path.Combine(minecraftRootPath, "libraries", mavenPath);
        }

        return Path.Combine(minecraftRootPath, value.Replace('/', Path.DirectorySeparatorChar));
    }

    private static ProcessStartInfo BuildStartInfo(
        string minecraftRootPath,
        string javaPath,
        LoaderProcessorStep processor,
        string processorJar,
        IReadOnlyList<string> classpath)
    {
        var startInfo = new ProcessStartInfo(javaPath)
        {
            WorkingDirectory = minecraftRootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var mainClass = ReadMainClass(processorJar);
        if (string.IsNullOrWhiteSpace(mainClass))
        {
            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(processorJar);
        }
        else
        {
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(string.Join(Path.PathSeparator, classpath.Prepend(processorJar)));
            startInfo.ArgumentList.Add(mainClass);
        }

        foreach (var argument in processor.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string ReadMainClass(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.GetEntry("META-INF/MANIFEST.MF");
            if (entry is null)
            {
                return "";
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            string? current = null;
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith(' ') && current is not null)
                {
                    current += line[1..];
                    continue;
                }

                if (current is not null)
                {
                    lines.Add(current);
                }

                current = line;
            }

            if (current is not null)
            {
                lines.Add(current);
            }

            const string key = "Main-Class:";
            return lines.FirstOrDefault(line => line.StartsWith(key, StringComparison.OrdinalIgnoreCase))?[key.Length..].Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveOutputPath(string minecraftRootPath, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsMavenCoordinate(value) || value.Contains("://", StringComparison.Ordinal))
        {
            return "";
        }

        return Path.IsPathRooted(value)
            ? value
            : Path.Combine(minecraftRootPath, value.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string TryCreateMavenPath(string coordinate)
    {
        var extension = "jar";
        var atIndex = coordinate.IndexOf('@', StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            extension = coordinate[(atIndex + 1)..];
            coordinate = coordinate[..atIndex];
        }

        if (!IsMavenCoordinate(coordinate))
        {
            return "";
        }

        var parts = coordinate.Split(':');
        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return Path.Combine(group, artifact, version, $"{artifact}-{version}{classifier}.{extension}");
    }

    private static bool IsMavenCoordinate(string value)
    {
        var parts = value.Split(':');
        return parts.Length >= 3 && !value.Contains(Path.DirectorySeparatorChar) && !value.Contains('/');
    }

    private static string UnwrapBracketedMavenCoordinate(string value)
    {
        if (value.Length > 2 && value[0] == '[' && value[^1] == ']')
        {
            return value[1..^1];
        }

        return value;
    }

    private static Task<string> TryReadToEnd(Process process, bool readError, CancellationToken cancellationToken)
    {
        try
        {
            var reader = readError ? process.StandardError : process.StandardOutput;
            return reader.ReadToEndAsync(cancellationToken);
        }
        catch
        {
            return Task.FromResult("");
        }
    }

    private static async Task IgnoreReadFailureAsync(Task<string> readTask)
    {
        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch
        {
            // Processor output is diagnostic only at this stage.
        }
    }

    private static LoaderProcessorRunResult CreateResult(
        IReadOnlyList<string> executed,
        IReadOnlyList<string> skipped,
        IReadOnlyList<string> missingInputs,
        IReadOnlyList<string> missingOutputs,
        IReadOnlyList<string> failed)
    {
        var success = missingInputs.Count == 0 && missingOutputs.Count == 0 && failed.Count == 0;
        return new LoaderProcessorRunResult(
            success,
            executed.ToList(),
            skipped.ToList(),
            missingInputs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            missingOutputs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            failed.ToList());
    }
}
