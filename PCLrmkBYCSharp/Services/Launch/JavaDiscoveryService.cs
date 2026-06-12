using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed partial class JavaDiscoveryService(IAppLoggerService logger) : IJavaDiscoveryService
{
    public async Task<IReadOnlyList<JavaEntry>> DiscoverAsync(string minecraftRootPath, string? instancePath, CancellationToken cancellationToken = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddJavaFromEnvironment(candidates);
        AddJavaFromPath(candidates);
        AddJavaUnderFolder(candidates, minecraftRootPath, maxDepth: 4);
        if (!string.IsNullOrWhiteSpace(instancePath))
        {
            AddJavaUnderFolder(candidates, instancePath, maxDepth: 3);
        }

        AddJavaUnderFolder(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), maxDepth: 4);
        AddJavaUnderFolder(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), maxDepth: 4);

        var result = new List<JavaEntry>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await InspectJavaAsync(candidate, cancellationToken: cancellationToken);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result
            .DistinctBy(entry => entry.PathJava, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(entry => entry.Is64Bit)
            .ThenByDescending(entry => entry.Version)
            .ToList();
    }

    public async Task<JavaEntry?> InspectJavaAsync(string javaPath, bool isUserImport = false, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeJavaPath(javaPath);
        if (!File.Exists(normalized))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(normalized, "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask + await errorTask;
            var parsed = ParseVersionOutput(output);
            return new JavaEntry(
                normalized,
                parsed.Version,
                !File.Exists(Path.Combine(Path.GetDirectoryName(normalized) ?? "", "javac.exe")),
                parsed.Is64Bit,
                isUserImport,
                HasEnvironment(normalized));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn($"检查 Java 失败：{normalized}，{ex.Message}");
            return null;
        }
    }

    public static (Version Version, bool Is64Bit) ParseVersionOutput(string output)
    {
        var lower = output.ToLowerInvariant();
        var match = VersionRegex().Match(output);
        if (!match.Success)
        {
            match = OpenJdkRegex().Match(output);
        }

        if (!match.Success)
        {
            throw new FormatException("未找到 Java 版本号");
        }

        var versionString = match.Groups[1].Value.Replace("_", ".").Split('-')[0];
        var parts = versionString.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 4)
        {
            parts = parts.Take(4).ToList();
        }

        while (parts.Count < 4)
        {
            if (versionString.StartsWith("1.", StringComparison.Ordinal))
            {
                parts.Add("0");
            }
            else
            {
                parts.Insert(0, "1");
            }
        }

        var version = new Version(
            int.Parse(parts[0]),
            int.Parse(parts[1]),
            int.Parse(parts[2]),
            int.Parse(parts[3]));
        if (version.Minor == 0)
        {
            version = new Version(1, version.Major, version.Build, version.Revision);
        }

        return (version, lower.Contains("64-bit") || lower.Contains("64 bit") || lower.Contains("x86_64"));
    }

    private static void AddJavaFromEnvironment(HashSet<string> candidates)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            AddJavaCandidate(candidates, Path.Combine(javaHome, "bin", "java.exe"));
        }
    }

    private static void AddJavaFromPath(HashSet<string> candidates)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            AddJavaCandidate(candidates, Path.Combine(entry, "java.exe"));
        }
    }

    private static void AddJavaUnderFolder(HashSet<string> candidates, string folder, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Search(folder, 0);

        void Search(string current, int depth)
        {
            AddJavaCandidate(candidates, Path.Combine(current, "java.exe"));
            AddJavaCandidate(candidates, Path.Combine(current, "bin", "java.exe"));
            if (depth >= maxDepth)
            {
                return;
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    var name = Path.GetFileName(child).ToLowerInvariant();
                    if (depth == 0 || name.Contains("java") || name.Contains("jdk") || name.Contains("jre") || name.Contains("runtime") || name.Contains("bin") || name.Contains("mc"))
                    {
                        Search(child, depth + 1);
                    }
                }
            }
            catch
            {
            }
        }
    }

    private static void AddJavaCandidate(HashSet<string> candidates, string javaPath)
    {
        if (File.Exists(javaPath))
        {
            candidates.Add(Path.GetFullPath(javaPath));
        }
    }

    private static string NormalizeJavaPath(string javaPath)
    {
        var path = Environment.ExpandEnvironmentVariables(javaPath.Trim().Trim('"'));
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, "java.exe");
        }

        return Path.GetFullPath(path);
    }

    private static bool HasEnvironment(string javaPath)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME") ?? "";
        return !string.IsNullOrWhiteSpace(javaHome)
            && javaPath.StartsWith(Path.GetFullPath(javaHome), StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("version \"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("openjdk ([0-9][^\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OpenJdkRegex();
}
