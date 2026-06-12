using System.IO;
using System.Text.Json;

namespace PCLrmkBYCSharp.Models;

public sealed record JavaEntry(
    string PathJava,
    Version Version,
    bool IsJre,
    bool Is64Bit,
    bool IsUserImport,
    bool HasEnvironment)
{
    public string PathFolder => Path.GetDirectoryName(PathJava) ?? "";

    public int MajorVersion => Version.Major == 1 ? Version.Minor : Version.Major;

    public string DisplayName
    {
        get
        {
            var version = Version.ToString();
            if (version.StartsWith("1.", StringComparison.Ordinal))
            {
                version = version[2..];
            }

            return $"{(IsJre ? "JRE" : "JDK")} {MajorVersion} ({version}){(Is64Bit ? "" : "，32 位")}{(IsUserImport ? "，手动导入" : "")}";
        }
    }
    public string ToPclSettingJson()
    {
        return JsonSerializer.Serialize(new JavaSettingEntry(
            PathFolder,
            Version.ToString(),
            IsJre,
            Is64Bit,
            IsUserImport));
    }

    public static string ToPclSettingJson(JavaEntry? entry)
    {
        return entry?.ToPclSettingJson() ?? "";
    }

    public static string ResolveSettingJavaPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (!document.RootElement.TryGetProperty("Path", out var pathElement))
            {
                return "";
            }

            var path = pathElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            if (Path.GetFileName(path).Equals("java.exe", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return Path.Combine(path, "java.exe");
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private sealed record JavaSettingEntry(
        string Path,
        string VersionString,
        bool IsJre,
        bool Is64Bit,
        bool IsUserImport);
}
