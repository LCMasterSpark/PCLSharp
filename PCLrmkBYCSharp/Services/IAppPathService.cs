namespace PCLrmkBYCSharp.Services;

public interface IAppPathService
{
    string AppDataDirectory { get; }

    string LogsDirectory { get; }

    string SettingsFilePath { get; }

    void EnsureCreated();
}
