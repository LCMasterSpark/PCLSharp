using System.IO;

namespace PCLrmkBYCSharp.Services;

public sealed class AppPathService : IAppPathService
{
    public AppPathService()
    {
        AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Plain Craft Launcher Sharp");
        LogsDirectory = Path.Combine(AppDataDirectory, "Logs");
        RuntimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCLSharp",
            "Runtime");
        SettingsFilePath = Path.Combine(AppDataDirectory, "settings.json");
    }

    public string AppDataDirectory { get; }

    public string LogsDirectory { get; }

    public string RuntimeDirectory { get; }

    public string SettingsFilePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
    }
}
