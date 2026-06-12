using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class InstancePageViewModel
{
    private void RefreshLocalModRows()
    {
        var selectedPath = SelectedLocalMod?.Mod.FilePath;
        var query = LocalModSearchText.Trim();
        var nameStyle = _settings.Get(AppSettingKeys.ToolModLocalNameStyle, 0);
        var visibleMods = _allLocalMods
            .Where(mod => MatchesLocalModFilter(mod) && MatchesLocalModSearch(mod, query))
            .ToList();
        PruneSelectedLocalModKeys(visibleMods);
        LocalMods.Clear();
        foreach (var mod in visibleMods)
        {
            _localModUpdateInfos.TryGetValue(mod.EnabledFileName, out var updateInfo);
            LocalMods.Add(new LocalModListRow(mod, nameStyle, _selectedLocalModKeys.Contains(mod.EnabledFileName), updateInfo));
        }

        SelectedLocalMod = LocalMods.FirstOrDefault(row => string.Equals(row.Mod.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? LocalMods.FirstOrDefault();
        RaiseLocalModPropertiesChanged();
    }

    private IReadOnlyList<LocalModFile> GetSelectedLocalMods()
    {
        return _allLocalMods
            .Where(mod => _selectedLocalModKeys.Contains(mod.EnabledFileName))
            .ToList();
    }

    private bool HasLocalModUpdate(LocalModFile mod)
    {
        return GetLocalModUpdate(mod)?.HasUpdate == true;
    }

    private LocalModUpdateInfo? GetLocalModUpdate(LocalModFile mod)
    {
        return _localModUpdateInfos.TryGetValue(mod.EnabledFileName, out var updateInfo)
            ? updateInfo
            : null;
    }

    private DownloadFile BuildLocalModUpdateDownloadFile(LocalModFile mod, LocalModUpdateInfo updateInfo)
    {
        var latestFile = updateInfo.LatestFile ?? throw new InvalidOperationException("缺少 Mod 更新文件");
        var fileName = SanitizeFileName(string.IsNullOrWhiteSpace(latestFile.FileName) ? mod.EnabledFileName : latestFile.FileName);
        if (!mod.IsEnabled)
        {
            fileName += ".disabled";
        }

        var localPath = Path.Combine(LocalModsDirectory, fileName);
        return new DownloadFile(
            [latestFile.Url],
            localPath,
            new DownloadFileCheck(ActualSize: latestFile.Size, Hash: latestFile.Sha1),
            SimulateBrowserHeaders: true);
    }

    private static void DeleteReplacedLocalMod(string oldPath, string newPath)
    {
        var oldFullPath = Path.GetFullPath(oldPath);
        var newFullPath = Path.GetFullPath(newPath);
        if (string.Equals(oldFullPath, newFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(oldFullPath))
        {
            File.Delete(oldFullPath);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var sanitized = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "mod.jar" : sanitized;
    }

    private void PruneSelectedLocalModKeys(IEnumerable<LocalModFile> visibleMods)
    {
        var visibleKeys = visibleMods.Select(mod => mod.EnabledFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedLocalModKeys.RemoveWhere(key => !visibleKeys.Contains(key));
    }

    private void RaiseLocalModPropertiesChanged()
    {
        OnPropertyChanged(nameof(LocalModCount));
        OnPropertyChanged(nameof(EnabledLocalModCount));
        OnPropertyChanged(nameof(DisabledLocalModCount));
        OnPropertyChanged(nameof(UpdateLocalModCount));
        OnPropertyChanged(nameof(SelectedLocalModCount));
        OnPropertyChanged(nameof(SelectedEnabledLocalModCount));
        OnPropertyChanged(nameof(SelectedDisabledLocalModCount));
        OnPropertyChanged(nameof(SelectedUpdateLocalModCount));
        OnPropertyChanged(nameof(HasSelectedLocalMods));
        OnPropertyChanged(nameof(LocalModSelectionSummary));
        OnPropertyChanged(nameof(HasLocalMods));
        OnPropertyChanged(nameof(LocalModsDirectory));
        SelectAllLocalModsCommand.NotifyCanExecuteChanged();
        CheckLocalModUpdatesCommand.NotifyCanExecuteChanged();
        UpdateSelectedLocalModsCommand.NotifyCanExecuteChanged();
        UpdateAllLocalModsCommand.NotifyCanExecuteChanged();
        ClearSelectedLocalModsCommand.NotifyCanExecuteChanged();
        EnableSelectedLocalModsCommand.NotifyCanExecuteChanged();
        DisableSelectedLocalModsCommand.NotifyCanExecuteChanged();
        DeleteSelectedLocalModsCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesLocalModFilter(LocalModFile mod)
    {
        return LocalModFilter switch
        {
            1 => mod.IsEnabled,
            2 => !mod.IsEnabled,
            _ => true
        };
    }

    private static bool MatchesLocalModSearch(LocalModFile mod, string query)
    {
        return string.IsNullOrWhiteSpace(query)
            || mod.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || mod.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || mod.Version.Contains(query, StringComparison.OrdinalIgnoreCase)
            || mod.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPreferredLoader(MinecraftInstance instance)
    {
        if (instance.Version.HasFabric)
        {
            return "fabric";
        }

        if (instance.Version.HasForge)
        {
            return "forge";
        }

        if (instance.Version.HasNeoForge)
        {
            return "neoforge";
        }

        return "";
    }

}
