using System.Runtime.CompilerServices;

namespace PCLrmkBYCSharp.Tests;

public sealed class SystemDialogUsageTests
{
    [Fact]
    public void AppCodeDoesNotUseSystemMessageBoxOrInputBox()
    {
        var projectRoot = GetProjectRoot();
        var sourceFiles = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}PCLrmkBYCSharp.Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var offenders = sourceFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => file.Text.Contains("MessageBox.Show", StringComparison.Ordinal)
                || file.Text.Contains("System.Windows.MessageBox", StringComparison.Ordinal)
                || file.Text.Contains("Interaction.InputBox", StringComparison.Ordinal)
                || file.Text.Contains("Microsoft.VisualBasic.Interaction", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(projectRoot, file.Path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string GetProjectRoot([CallerFilePath] string filePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PCLrmkBYCSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the PCLrmkBYCSharp solution root.");
    }
}
