namespace PCLrmkBYCSharp.Models;

public interface ISettingDefinition
{
    string Key { get; }

    Type ValueType { get; }

    object? DefaultValue { get; }

    SettingSource Source { get; }
}
