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
    private void RestoreSelection()
    {
        var savedName = _settings.Get(AppSettingKeys.InstanceManageSelectedName, "");
        if (string.IsNullOrWhiteSpace(savedName))
        {
            savedName = GetLaunchInstanceName();
        }

        if (!_suppressHiddenSelectionReveal
            && !ShowHiddenInstances
            && !string.IsNullOrWhiteSpace(savedName)
            && _allInstances.Any(instance => instance.IsHidden && string.Equals(instance.Name, savedName, StringComparison.OrdinalIgnoreCase)))
        {
            _isRestoringSelection = true;
            try
            {
                ShowHiddenInstances = true;
            }
            finally
            {
                _isRestoringSelection = false;
            }

            RefreshVisibleInstances();
        }

        SelectedInstance = Instances.FirstOrDefault(instance => string.Equals(instance.Name, savedName, StringComparison.OrdinalIgnoreCase))
            ?? Instances.FirstOrDefault();
    }

    private void RefreshVisibleInstances()
    {
        Instances.Clear();
        var query = InstanceSearchText.Trim();
        foreach (var instance in _allInstances
            .Where(instance => ShowHiddenInstances ? instance.IsHidden : !instance.IsHidden)
            .Where(instance => MatchesInstanceSearch(instance, query)))
        {
            Instances.Add(instance);
        }

        RefreshInstanceRows();
        SyncSelectedInstanceRow(SelectedInstance?.Name);
        OnPropertyChanged(nameof(InstanceCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(HiddenCount));
    }

    private void RefreshInstanceRows()
    {
        InstanceRows.Clear();
        var launchInstanceName = GetLaunchInstanceName();
        var managedInstanceName = SelectedInstance?.Name ?? _settings.Get(AppSettingKeys.InstanceManageSelectedName, "");
        foreach (var group in BuildInstanceGroups(Instances))
        {
            if (group.Instances.Count == 0)
            {
                continue;
            }

            var title = group.Title == "收藏夹" ? group.Title : $"{group.Title} ({group.Instances.Count})";
            InstanceRows.Add(new InstanceListRow(title));
            foreach (var instance in group.Instances)
            {
                InstanceRows.Add(new InstanceListRow(instance, launchInstanceName, managedInstanceName));
            }
        }

        OnPropertyChanged(nameof(HasInstanceRows));
        OnPropertyChanged(nameof(InstanceListVisibleCount));
        OnPropertyChanged(nameof(InstanceListSummary));
        OnPropertyChanged(nameof(InstanceListEmptyText));
        OnPropertyChanged(nameof(CurrentLaunchVersionName));
        OnPropertyChanged(nameof(VersionManagementSummary));
        OnPropertyChanged(nameof(IsSelectedInstanceLaunchVersion));
        OnPropertyChanged(nameof(SelectedLaunchActionText));
        UseSelectedInstanceForLaunchCommand.NotifyCanExecuteChanged();
    }

    private void SyncSelectedInstanceRow(string? instanceName)
    {
        _isSyncingInstanceRowSelection = true;
        try
        {
            SelectedInstanceRow = string.IsNullOrWhiteSpace(instanceName)
                ? null
                : InstanceRows.FirstOrDefault(row =>
                    row.Instance is not null
                    && string.Equals(row.Instance.Name, instanceName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isSyncingInstanceRowSelection = false;
        }
    }

    private void UpdateInstanceRowRoles()
    {
        var launchInstanceName = GetLaunchInstanceName();
        var managedInstanceName = SelectedInstance?.Name ?? _settings.Get(AppSettingKeys.InstanceManageSelectedName, "");
        foreach (var row in InstanceRows)
        {
            row.UpdateRoleNames(launchInstanceName, managedInstanceName);
        }

        OnPropertyChanged(nameof(CurrentLaunchVersionName));
        OnPropertyChanged(nameof(VersionManagementSummary));
        OnPropertyChanged(nameof(IsSelectedInstanceLaunchVersion));
        OnPropertyChanged(nameof(SelectedLaunchActionText));
        UseSelectedInstanceForLaunchCommand.NotifyCanExecuteChanged();
    }

    private string GetLaunchInstanceName()
    {
        var name = _selections.ReadSelectedInstanceName(MinecraftRootPath);
        return string.IsNullOrWhiteSpace(name) ? _settings.Get(AppSettingKeys.SelectedInstanceName, "") : name;
    }

    private static bool MatchesInstanceSearch(MinecraftInstance instance, string query)
    {
        return string.IsNullOrWhiteSpace(query)
            || instance.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.DisplayVersion.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.LoaderSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.DisplayInfo.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.GroupName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || instance.State.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<(string Title, List<MinecraftInstance> Instances)> BuildInstanceGroups(IReadOnlyList<MinecraftInstance> source)
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
            ("常规版本", SortInstances(source.Where(IsOriginalLike)).ToList()),
            ("愚人节版本", SortInstances(source.Where(instance => instance.DisplayType == MinecraftInstanceDisplayType.Fool)).ToList()),
            ("不常用版本", SortInstances(source.Where(instance => instance.DisplayType == MinecraftInstanceDisplayType.Rubbish)).ToList()),
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

    private static bool IsOriginalLike(MinecraftInstance instance)
    {
        return !instance.HasError
            && !instance.IsHidden
            && !IsModLike(instance)
            && instance.DisplayType is MinecraftInstanceDisplayType.Auto or MinecraftInstanceDisplayType.OriginalLike;
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

    private void SyncVersionSortModeFromSettings()
    {
        var saved = NormalizeVersionSortMode(_settings.Get(AppSettingKeys.VersionSortMode, VersionSortMode));
        if (VersionSortMode != saved)
        {
            VersionSortMode = saved;
        }
    }

    private void SyncMinecraftRootPathFromSettings()
    {
        var savedRoot = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        var targetRoot = string.IsNullOrWhiteSpace(savedRoot) ? _minecraftDiscovery.GetDefaultMinecraftRoot() : savedRoot;
        if (!string.Equals(MinecraftRootPath, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            MinecraftRootPath = targetRoot;
        }
    }

    private void LoadLaunchSettings(string? instanceName)
    {
        _isLoadingLaunchSettings = true;
        MinMemoryMb = GetInstanceSetting(instanceName, AppSettingKeys.LaunchMinMemoryMb, 512);
        MaxMemoryMb = GetInstanceSetting(instanceName, AppSettingKeys.LaunchMaxMemoryMb, 4096);
        VersionRamType = GetInstanceSetting(instanceName, AppSettingKeys.VersionRamType, 2);
        VersionRamCustom = GetInstanceSetting(instanceName, AppSettingKeys.VersionRamCustom, 15);
        LaunchWindowWidth = GetInstanceSetting(instanceName, AppSettingKeys.LaunchWindowWidth, 854);
        LaunchWindowHeight = GetInstanceSetting(instanceName, AppSettingKeys.LaunchWindowHeight, 480);
        VersionArgumentTitle = GetInstanceSetting(instanceName, AppSettingKeys.VersionArgumentTitle, "");
        VersionCustomInfo = GetInstanceSetting(instanceName, AppSettingKeys.VersionArgumentInfo, SelectedInstance?.CustomInfo ?? "");
        ExtraJvmArgs = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceJvm, "");
        ExtraGameArgs = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceGame, "");
        VersionAdvanceRun = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceRun, "");
        VersionAdvanceRunWait = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceRunWait, true);
        VersionJavaPath = GetInstanceSetting(instanceName, AppSettingKeys.VersionArgumentJavaSelect, "使用全局设置");
        ServerIp = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerEnter, "");
        VersionServerLogin = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerLogin, 0);
        VersionServerNide = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerNide, "");
        VersionServerAuthServer = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerAuthServer, "");
        VersionServerAuthRegister = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerAuthRegister, "");
        VersionServerAuthName = GetInstanceSetting(instanceName, AppSettingKeys.VersionServerAuthName, "");
        VersionGc = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceGC, 0);
        VersionRamOptimize = GetInstanceSetting(instanceName, AppSettingKeys.VersionRamOptimize, 0);
        VersionDisplayType = SelectedInstance?.DisplayType ?? MinecraftInstanceDisplayType.Auto;
        DisableJlw = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceDisableJLW, false);
        DisableLua = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceDisableLUA, false);
        DisableModUpdate = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceDisableModUpdate, false);
        IgnoreJavaCompatibility = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceJava, false);
        DisableFileCheck = GetInstanceSetting(instanceName, AppSettingKeys.VersionAdvanceAssetsV2, false);
        VersionIsolationEnabled = SelectedInstance is not null && _gameDirectories.Resolve(SelectedInstance).IsIsolated;
        UpdateGameDirectoryPath();
        _isLoadingLaunchSettings = false;
    }

    private string ResolveSelectedGameDirectory()
    {
        return SelectedInstance is null ? MinecraftRootPath : ResolveGameDirectory(SelectedInstance);
    }

    private string ResolveGameDirectory(MinecraftInstance instance)
    {
        return _gameDirectories.GetPath(instance, VersionIsolationEnabled);
    }

    private static string BuildSelectedInstanceDetail(MinecraftInstance instance)
    {
        var detail = $"{instance.Name} / {instance.DisplayVersion} / {instance.LoaderSummary}";
        return string.IsNullOrWhiteSpace(instance.CustomInfo)
            ? detail
            : detail + " / " + instance.CustomInfo;
    }

    private string BuildSelectedInstanceOverview(MinecraftInstance instance)
    {
        var role = IsSelectedInstanceLaunchVersion ? "启动并正在管理" : "正在管理";
        var state = instance.HasError
            ? "异常：" + EmptyToDash(instance.ErrorMessage)
            : "可启动";
        var flags = new List<string>();
        if (instance.IsStar)
        {
            flags.Add("已收藏");
        }

        if (instance.IsHidden)
        {
            flags.Add("已隐藏");
        }

        var flagText = flags.Count == 0 ? "无" : string.Join("，", flags);
        var isolation = VersionIsolationEnabled ? "版本隔离" : "公共 .minecraft";
        return string.Join(Environment.NewLine, new[]
        {
            "角色：" + role,
            "状态：" + state,
            "分类：" + instance.GroupName,
            "标记：" + flagText,
            "游戏目录模式：" + isolation,
            "游戏目录：" + ResolveGameDirectory(instance),
            "版本目录：" + instance.VersionPath
        });
    }

    private string BuildSelectedInstanceTechnicalDetail(MinecraftInstance instance)
    {
        var version = instance.Version;
        var parts = new List<string>
        {
            "版本 ID：" + EmptyToDash(version.Id),
            "显示版本：" + EmptyToDash(instance.DisplayVersion),
            "版本类型：" + EmptyToDash(version.Type),
            "加载器：" + instance.LoaderSummary,
            "继承版本：" + EmptyToDash(version.InheritsFrom),
            "主类：" + EmptyToDash(version.MainClass),
            "资源索引：" + EmptyToDash(version.AssetsIndex),
            "依赖库：" + version.LibraryCount + " 个",
            "参数格式：" + GetArgumentFormatText(version),
            "发布时间：" + FormatVersionTime(version.ReleaseTime),
            "更新时间：" + FormatVersionTime(version.Time),
            "版本目录：" + instance.VersionPath,
            "游戏目录：" + ResolveGameDirectory(instance),
            "JSON 文件：" + instance.JsonPath
        };

        if (!string.IsNullOrWhiteSpace(instance.ErrorMessage))
        {
            parts.Insert(0, "状态：" + instance.ErrorMessage);
        }
        else
        {
            parts.Insert(0, "状态：可启动");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string GetArgumentFormatText(MinecraftVersionInfo version)
    {
        if (version.HasModernArguments && version.HasLegacyMinecraftArguments)
        {
            return "新版 arguments + 旧版 minecraftArguments";
        }

        if (version.HasModernArguments)
        {
            return "新版 arguments";
        }

        return version.HasLegacyMinecraftArguments ? "旧版 minecraftArguments" : "未声明";
    }

    private static string FormatVersionTime(DateTimeOffset? time)
    {
        return time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    private static string EmptyToDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private void UpdateGameDirectoryPath()
    {
        GameDirectoryPath = SelectedInstance is null ? "" : ResolveGameDirectory(SelectedInstance);
    }

    private T GetInstanceSetting<T>(string? instanceName, string key, T defaultValue)
    {
        var fallback = _settings.Get(key, defaultValue);
        return string.IsNullOrWhiteSpace(instanceName)
            ? fallback
            : _settings.Get(GetInstanceSettingKey(instanceName, key), fallback);
    }

    private void SetInstanceSetting<T>(string instanceName, string key, T value)
    {
        _settings.Set(GetInstanceSettingKey(instanceName, key), value);
    }

    private int CountSavedInstanceLaunchSettings(string instanceName)
    {
        return InstanceLaunchSettingKeys.Count(key => _settings.HasSaved(GetInstanceSettingKey(instanceName, key)));
    }

    private static string GetInstanceSettingKey(string instanceName, string key)
    {
        return $"Instance.{instanceName}.{key}";
    }

}
