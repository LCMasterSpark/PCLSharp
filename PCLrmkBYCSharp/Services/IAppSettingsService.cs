using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IAppSettingsService
{
    event EventHandler<AppSettingChangedEventArgs>? SettingChanged;

    T Get<T>(string key);

    T Get<T>(string key, T defaultValue);

    void Set<T>(string key, T value);

    void Reset(string key);

    bool HasSaved(string key);

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
