namespace PCLrmkBYCSharp.Models;

public sealed class AppSettingChangedEventArgs(string key, object? value) : EventArgs
{
    public string Key { get; } = key;

    public object? Value { get; } = value;
}
