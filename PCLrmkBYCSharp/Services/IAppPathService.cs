namespace PCLrmkBYCSharp.Services;

public interface IAppPathService
{
    string AppDataDirectory { get; }

    string LogsDirectory { get; }

    string RuntimeDirectory { get; }

    string SettingsFilePath { get; }

    void EnsureCreated();
}
