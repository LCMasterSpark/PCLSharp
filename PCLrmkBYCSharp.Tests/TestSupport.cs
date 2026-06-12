using System.IO;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PCLrmkBYCSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class TestAppPathService : IAppPathService
{
    public TestAppPathService(string root)
    {
        AppDataDirectory = root;
        LogsDirectory = System.IO.Path.Combine(root, "Logs");
        SettingsFilePath = System.IO.Path.Combine(root, "settings.json");
    }

    public string AppDataDirectory { get; }

    public string LogsDirectory { get; }

    public string SettingsFilePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

internal sealed class NullLoggerService : IAppLoggerService
{
    public void Initialize()
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(Exception exception, string message)
    {
    }
}

internal sealed class NullFileDialogService : IFileDialogService
{
    public string? PickFolder(string title, string initialDirectory) => null;

    public string? PickJavaExecutable(string initialDirectory) => null;

    public string? PickSkinFile(string initialDirectory) => null;

    public string? PickModpackFile(string initialDirectory) => null;

    public IReadOnlyList<string> PickModFiles(string initialDirectory) => [];

    public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter) => null;
}
