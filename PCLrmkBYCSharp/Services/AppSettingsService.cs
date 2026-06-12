using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<string, object?> _values = new();
    private readonly IReadOnlyDictionary<string, ISettingDefinition> _definitions;
    private readonly IAppPathService _paths;

    public AppSettingsService(IAppPathService paths)
    {
        _paths = paths;
        _definitions = AppSettingKeys.Definitions.ToDictionary(definition => definition.Key);
    }

    public event EventHandler<AppSettingChangedEventArgs>? SettingChanged;

    public T Get<T>(string key)
    {
        if (_definitions.TryGetValue(key, out var definition) && definition.DefaultValue is T typedDefault)
        {
            return Get(key, typedDefault);
        }

        return Get<T>(key, default!);
    }

    public T Get<T>(string key, T defaultValue)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return ConvertValue(value, defaultValue);
    }

    public void Set<T>(string key, T value)
    {
        _values[key] = value;
        SettingChanged?.Invoke(this, new AppSettingChangedEventArgs(key, value));
    }

    public void Reset(string key)
    {
        _values.TryRemove(key, out _);
        SettingChanged?.Invoke(this, new AppSettingChangedEventArgs(key, null));
    }

    public bool HasSaved(string key) => _values.ContainsKey(key);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.SettingsFilePath))
        {
            return;
        }

        JsonDocument document;
        try
        {
            await using var stream = File.OpenRead(_paths.SettingsFilePath);
            document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            _values.Clear();
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var valueType = _definitions.TryGetValue(property.Name, out var definition)
                    ? definition.ValueType
                    : typeof(object);

                try
                {
                    _values[property.Name] = JsonSerializer.Deserialize(property.Value.GetRawText(), valueType, JsonOptions);
                }
                catch (JsonException)
                {
                    _values.TryRemove(property.Name, out _);
                }
            }
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureCreated();
        var values = _values.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
        var json = JsonSerializer.Serialize(values, JsonOptions);
        File.WriteAllText(_paths.SettingsFilePath, json);
        return Task.CompletedTask;
    }

    private static T ConvertValue<T>(object? value, T defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        if (value is JsonElement element)
        {
            var converted = element.Deserialize<T>(JsonOptions);
            return converted is null ? defaultValue : converted;
        }

        try
        {
            if (typeof(T).IsEnum)
            {
                return (T)Enum.Parse(typeof(T), value.ToString() ?? string.Empty, ignoreCase: true);
            }

            return (T)System.Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }
}
