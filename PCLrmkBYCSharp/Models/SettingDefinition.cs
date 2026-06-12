namespace PCLrmkBYCSharp.Models;

public sealed record SettingDefinition<T>(
    string Key,
    T Default,
    SettingSource Source = SettingSource.Global) : ISettingDefinition
{
    public Type ValueType => typeof(T);

    object? ISettingDefinition.DefaultValue => Default;
}
