using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public sealed class HelpService : IHelpService
{
    private readonly string _helpZipPath;
    private readonly IReadOnlyList<string> _customHelpDirectories;
    private readonly IAppLoggerService _logger;

    public HelpService(IAppLoggerService logger, string? helpZipPath = null, IEnumerable<string>? customHelpDirectories = null)
    {
        _logger = logger;
        _helpZipPath = string.IsNullOrWhiteSpace(helpZipPath)
            ? Path.Combine(AppContext.BaseDirectory, "Resources", "Help.zip")
            : helpZipPath;
        _customHelpDirectories = (customHelpDirectories ?? [Path.Combine(AppContext.BaseDirectory, "PCL", "Help")])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<HelpEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<HelpEntry>();
        var ignoreRules = new List<string>();
        foreach (var directory in _customHelpDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadCustomDirectoryAsync(directory, entries, ignoreRules, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(_helpZipPath))
        {
            using var archive = ZipFile.OpenRead(_helpZipPath);
            foreach (var jsonEntry in archive.Entries
                .Where(entry => entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Where(entry => !IsInHiddenFolder(entry.FullName))
                .Where(entry => !IsIgnored(entry.FullName, ignoreRules))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await TryReadEntryAsync(archive, jsonEntry, cancellationToken).ConfigureAwait(false);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }
        else
        {
            _logger.Warn("未找到内置帮助包：" + _helpZipPath);
        }

        _logger.Info($"已加载帮助条目：{entries.Count}");
        return entries;
    }

    public IReadOnlyList<HelpEntry> Search(IReadOnlyList<HelpEntry> entries, string query, int maxCount = 30)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return entries
                .Where(entry => entry.ShowInPublic)
                .OrderBy(entry => entry.TypeText, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();
        }

        var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries
            .Where(entry => entry.ShowInSearch && entry.ShowInPublic)
            .Select(entry => new
            {
                Entry = entry,
                Score = Score(entry, parts)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(item => item.Entry)
            .ToList();
    }

    private async Task<HelpEntry?> TryReadEntryAsync(ZipArchive archive, ZipArchiveEntry jsonEntry, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = jsonEntry.Open();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var title = GetString(root, "Title");
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var isEvent = GetBool(root, "IsEvent");
            var xamlContent = "";
            if (!isEvent)
            {
                var xamlPath = Path.ChangeExtension(jsonEntry.FullName, ".xaml")?.Replace('\\', '/');
                var xamlEntry = string.IsNullOrWhiteSpace(xamlPath)
                    ? null
                    : archive.GetEntry(xamlPath);
                if (xamlEntry is null)
                {
                    _logger.Warn("帮助条目缺少对应 XAML：" + jsonEntry.FullName);
                    return null;
                }

                await using var xamlStream = xamlEntry.Open();
                using var reader = new StreamReader(xamlStream);
                xamlContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            return new HelpEntry(
                title,
                GetString(root, "Description"),
                GetString(root, "Keywords"),
                ReadTypes(root),
                jsonEntry.FullName,
                isEvent,
                GetString(root, "EventType"),
                GetString(root, "EventData"),
                xamlContent,
                GetBool(root, "ShowInSearch", true),
                GetBool(root, "ShowInPublic", true),
                GetBool(root, "ShowInSnapshot", true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn("加载帮助条目失败：" + jsonEntry.FullName + "：" + ex.Message);
            return null;
        }
    }

    private async Task LoadCustomDirectoryAsync(
        string directory,
        List<HelpEntry> entries,
        List<string> ignoreRules,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var ignoreFile in Directory.EnumerateFiles(directory, "*.helpignore", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var line in await File.ReadAllLinesAsync(ignoreFile, cancellationToken).ConfigureAwait(false))
            {
                var rule = line.Split('#')[0].Trim();
                if (!string.IsNullOrWhiteSpace(rule))
                {
                    ignoreRules.Add(rule);
                }
            }
        }

        foreach (var jsonPath in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await TryReadFileEntryAsync(directory, jsonPath, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }
    }

    private async Task<HelpEntry?> TryReadFileEntryAsync(string rootDirectory, string jsonPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(jsonPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var title = GetString(root, "Title");
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var isEvent = GetBool(root, "IsEvent");
            var xamlContent = "";
            if (!isEvent)
            {
                var xamlPath = Path.ChangeExtension(jsonPath, ".xaml");
                if (string.IsNullOrWhiteSpace(xamlPath) || !File.Exists(xamlPath))
                {
                    _logger.Warn("帮助条目缺少对应 XAML：" + jsonPath);
                    return null;
                }

                xamlContent = await File.ReadAllTextAsync(xamlPath, cancellationToken).ConfigureAwait(false);
            }

            return new HelpEntry(
                title,
                GetString(root, "Description"),
                GetString(root, "Keywords"),
                ReadTypes(root),
                Path.GetRelativePath(rootDirectory, jsonPath).Replace('\\', '/'),
                isEvent,
                GetString(root, "EventType"),
                GetString(root, "EventData"),
                xamlContent,
                GetBool(root, "ShowInSearch", true),
                GetBool(root, "ShowInPublic", true),
                GetBool(root, "ShowInSnapshot", true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn("加载自定义帮助条目失败：" + jsonPath + "：" + ex.Message);
            return null;
        }
    }

    private static int Score(HelpEntry entry, IReadOnlyList<string> parts)
    {
        var score = 0;
        foreach (var part in parts)
        {
            if (entry.Title.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                score += 6;
            }

            if (entry.Keywords.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            if (entry.Description.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (entry.Types.Any(type => type.Contains(part, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        return score;
    }

    private static IReadOnlyList<string> ReadTypes(JsonElement root)
    {
        if (!root.TryGetProperty("Types", out var types) || types.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return types.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static bool IsInHiddenFolder(string zipPath)
    {
        return zipPath.Split(["/"], StringSplitOptions.RemoveEmptyEntries)
            .SkipLast(1)
            .Any(part => part.StartsWith(".", StringComparison.Ordinal));
    }

    private static bool IsIgnored(string zipPath, IReadOnlyList<string> ignoreRules)
    {
        foreach (var rule in ignoreRules)
        {
            try
            {
                if (Regex.IsMatch(zipPath, rule, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    return true;
                }
            }
            catch
            {
                if (zipPath.Contains(rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static bool GetBool(JsonElement root, string propertyName, bool defaultValue = false)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }
}
