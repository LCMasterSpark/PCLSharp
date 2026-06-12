using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LaunchPageViewModel
{
    private async Task PrepareLaunchInputsAsync()
    {
        SaveLaunchSettings();
        if (Instances.Count == 0)
        {
            await RefreshInstancesCoreAsync();
        }

        if (JavaEntries.Count == 0)
        {
            await ScanJavaCoreAsync();
        }
    }

    private async Task RefreshInstancesCoreAsync()
    {
        _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
        var instances = await _minecraftDiscovery.ScanAsync(MinecraftRootPath);
        await InvokeOnUiAsync(() => ApplyScannedInstances(instances));
    }

    private void ApplyScannedInstances(IReadOnlyList<MinecraftInstance> instances)
    {
        _allInstances.Clear();
        _allInstances.AddRange(instances);
        Instances.Clear();
        foreach (var instance in instances)
        {
            Instances.Add(instance);
        }
        RefreshVersionSelectorRows();
        OnPropertyChanged(nameof(HasInstances));

        var savedName = _selections.ReadSelectedInstanceName(MinecraftRootPath);
        if (string.IsNullOrWhiteSpace(savedName))
        {
            savedName = _settings.Get(AppSettingKeys.SelectedInstanceName, "");
        }
        var savedVersionMissing = !string.IsNullOrWhiteSpace(savedName);
        var matched = _allInstances.FirstOrDefault(instance => string.Equals(instance.Name, savedName, StringComparison.OrdinalIgnoreCase));
        var fallback = matched
            ?? _allInstances.FirstOrDefault(instance => !instance.IsHidden)
            ?? _allInstances.FirstOrDefault();
        SelectedInstance = fallback;
        UpdateVersionSelectorRowRoles();
        if (fallback is not null)
        {
            _settings.Set(AppSettingKeys.SelectedInstanceName, fallback.Name);
            _selections.WriteSelectedInstanceName(MinecraftRootPath, fallback.Name);
        }
        else
        {
            _settings.Set(AppSettingKeys.SelectedInstanceName, "");
            _selections.WriteSelectedInstanceName(MinecraftRootPath, "");
        }

        OnPropertyChanged(nameof(CurrentVersionTitle));
        OnPropertyChanged(nameof(CurrentVersionSubtitle));
        OnPropertyChanged(nameof(SelectedInstanceSummary));
        OnPropertyChanged(nameof(InstanceLaunchSettingsSummary));
        OnPropertyChanged(nameof(HasSelectedInstance));
        OnPropertyChanged(nameof(HasNoSelectedInstance));
        var scanStatusMessage = savedVersionMissing && matched is null
            ? fallback is null
                ? $"原启动版本 {savedName} 不存在，当前没有可用版本"
                : $"原启动版本 {savedName} 不存在，已切换到 {fallback.Name}"
            : $"实例扫描完成：{Instances.Count} 个";
        _selectionRecoveryStatusMessage = savedVersionMissing && matched is null ? scanStatusMessage : string.Empty;
        StatusMessage = scanStatusMessage;
    }

    private void RefreshVersionSelectorRows()
    {
        VersionSelectorRows.Clear();
        var query = VersionSearchText.Trim();
        var source = _allInstances
            .Where(instance => ShowHiddenVersions ? instance.IsHidden : !instance.IsHidden)
            .Where(instance => MatchesVersionSearch(instance, query))
            .ToList();

        foreach (var group in BuildVersionSelectorGroups(source))
        {
            if (group.Instances.Count == 0)
            {
                continue;
            }

            var title = group.Title == "收藏夹" ? group.Title : $"{group.Title} ({group.Instances.Count})";
            VersionSelectorRows.Add(new LaunchVersionListRow(title));
            foreach (var instance in group.Instances)
            {
                VersionSelectorRows.Add(new LaunchVersionListRow(instance, SelectedInstance?.Name));
            }
        }

        _isSyncingVersionSelectorRow = true;
        try
        {
            SelectedVersionSelectorRow = VersionSelectorRows.FirstOrDefault(row =>
                row.Instance is not null
                && string.Equals(row.Instance.Name, SelectedInstance?.Name, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isSyncingVersionSelectorRow = false;
        }

        OnPropertyChanged(nameof(HasVersionSelectorRows));
        OnPropertyChanged(nameof(VersionSelectorVisibleCount));
        OnPropertyChanged(nameof(VersionSelectorSummary));
        OnPropertyChanged(nameof(VersionSelectorEmptyText));
    }

    private void UpdateVersionSelectorRowRoles()
    {
        LaunchVersionListRow? selectedRow = null;
        var selectedName = SelectedInstance?.Name;

        for (var i = 0; i < VersionSelectorRows.Count; i++)
        {
            var row = VersionSelectorRows[i];
            if (row.Instance is null)
            {
                continue;
            }

            row.RefreshCurrentLaunchVersion(selectedName);
            if (string.Equals(row.Instance.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            {
                selectedRow = row;
            }
        }

        _isSyncingVersionSelectorRow = true;
        try
        {
            SelectedVersionSelectorRow = selectedRow;
        }
        finally
        {
            _isSyncingVersionSelectorRow = false;
        }

        OnPropertyChanged(nameof(VersionSelectorSummary));
    }

    private static bool MatchesVersionSearch(MinecraftInstance instance, string query)
    {
        return string.IsNullOrWhiteSpace(query)
            || instance.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.DisplayVersion.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.LoaderSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.DisplayInfo.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSelectedInstanceSummary(MinecraftInstance instance)
    {
        var summary = $"{instance.Name} / {instance.DisplayVersion} / {instance.LoaderSummary}";
        return string.IsNullOrWhiteSpace(instance.CustomInfo)
            ? summary
            : summary + " / " + instance.CustomInfo;
    }

    private IReadOnlyList<(string Title, List<MinecraftInstance> Instances)> BuildVersionSelectorGroups(IReadOnlyList<MinecraftInstance> source)
    {
        if (source.Count == 0)
        {
            return [];
        }

        if (source.All(instance => instance.IsHidden))
        {
            return [("隐藏的版本", SortInstances(source).ToList())];
        }

        var apiInstances = SortInstances(source.Where(IsModLike)).ToList();
        var groups = new List<(string Title, List<MinecraftInstance> Instances)>
        {
            ("收藏夹", SortInstances(source.Where(instance => instance.IsStar)).ToList()),
            (GetApiGroupTitle(apiInstances), apiInstances),
            ("常规版本", SortInstances(source.Where(instance => instance.GroupName == "常规版本")).ToList()),
            ("愚人节版本", SortInstances(source.Where(instance => instance.GroupName == "愚人节版本")).ToList()),
            ("不常用版本", SortInstances(source.Where(instance => instance.GroupName == "不常用版本")).ToList()),
            ("错误的版本", SortInstances(source.Where(instance => instance.HasError)).ToList())
        };

        var used = groups.SelectMany(group => group.Instances).ToHashSet();
        var other = SortInstances(source.Where(instance => !used.Contains(instance))).ToList();
        if (other.Count > 0)
        {
            groups.Insert(3, ("其他版本", other));
        }

        return groups;
    }

    private static bool IsModLike(MinecraftInstance instance)
    {
        return !instance.HasError
            && !instance.IsHidden
            && (instance.Version.HasForge
                || instance.Version.HasNeoForge
                || instance.Version.HasFabric
                || instance.Version.HasOptiFine
                || instance.DisplayType == MinecraftInstanceDisplayType.Api);
    }

    private static string GetApiGroupTitle(IReadOnlyList<MinecraftInstance> instances)
    {
        if (instances.Count == 0)
        {
            return "可安装 Mod 的版本";
        }

        var hasForge = instances.Any(instance => instance.Version.HasForge);
        var hasNeoForge = instances.Any(instance => instance.Version.HasNeoForge);
        var hasFabric = instances.Any(instance => instance.Version.HasFabric);
        var hasOptiFine = instances.Any(instance => instance.Version.HasOptiFine);
        var exclusiveCount = new[] { hasForge, hasNeoForge, hasFabric, hasOptiFine }.Count(value => value);
        if (exclusiveCount == 1)
        {
            if (hasForge)
            {
                return "Forge 版本";
            }

            if (hasNeoForge)
            {
                return "NeoForge 版本";
            }

            if (hasFabric)
            {
                return "Fabric 版本";
            }

            if (hasOptiFine)
            {
                return "OptiFine 版本";
            }
        }

        return "可安装 Mod 的版本";
    }

    private IEnumerable<MinecraftInstance> SortInstances(IEnumerable<MinecraftInstance> instances)
    {
        return VersionSortMode switch
        {
            1 => instances.OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase),
            2 => instances.OrderByDescending(instance => instance.Name, StringComparer.OrdinalIgnoreCase),
            _ => instances
                .OrderByDescending(instance => instance.Version.ReleaseTime)
                .ThenBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static int NormalizeVersionSortMode(int value)
    {
        return value is >= 0 and <= 2 ? value : 0;
    }

    private static string NormalizeServerIp(string value)
    {
        return value.Replace('：', ':').Replace('。', '.');
    }

    private void SyncVersionSortModeFromSettings()
    {
        var saved = NormalizeVersionSortMode(_settings.Get(AppSettingKeys.VersionSortMode, VersionSortMode));
        if (VersionSortMode != saved)
        {
            VersionSortMode = saved;
        }
    }

    private bool SyncMinecraftRootPathFromSettings()
    {
        var savedRoot = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        var targetRoot = string.IsNullOrWhiteSpace(savedRoot) ? _minecraftDiscovery.GetDefaultMinecraftRoot() : savedRoot;
        if (string.Equals(MinecraftRootPath, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        MinecraftRootPath = targetRoot;
        return true;
    }

}
