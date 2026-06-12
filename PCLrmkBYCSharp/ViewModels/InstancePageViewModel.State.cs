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
    private void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(Instances, _instancesSync);
        BindingOperations.EnableCollectionSynchronization(InstanceRows, _instanceRowsSync);
        BindingOperations.EnableCollectionSynchronization(LocalMods, _localModsSync);
        BindingOperations.EnableCollectionSynchronization(FileCompletionDetails, _fileCompletionDetailsSync);
        BindingOperations.EnableCollectionSynchronization(MinecraftRootFolders, _minecraftRootFoldersSync);
    }

    public ObservableCollection<MinecraftInstance> Instances { get; } = [];

    public ObservableCollection<InstanceListRow> InstanceRows { get; } = [];

    public ObservableCollection<LocalModListRow> LocalMods { get; } = [];

    public ObservableCollection<string> FileCompletionDetails { get; } = [];

    public ObservableCollection<MinecraftRootFolder> MinecraftRootFolders { get; } = [];

    public IReadOnlyList<IntOption> VersionGcOptions { get; } =
    [
        new(0, "跟随全局设置"),
        new(1, "尽量使用 ZGC"),
        new(2, "尽量使用分代 ZGC"),
        new(3, "标准 G1GC"),
        new(5, "调优 G1GC"),
        new(4, "不指定（可自定义）")
    ];

    public IReadOnlyList<IntOption> VersionRamOptimizeOptions { get; } =
    [
        new(0, "跟随全局设置"),
        new(1, "开启"),
        new(2, "关闭")
    ];

    public IReadOnlyList<IntOption> VersionRamTypeOptions { get; } =
    [
        new(2, "跟随全局设置"),
        new(0, "自动配置"),
        new(1, "自定义")
    ];

    public IReadOnlyList<IntOption> VersionServerLoginOptions { get; } =
    [
        new(0, "正版登录或离线登录"),
        new(1, "仅正版登录"),
        new(2, "仅离线登录"),
        new(3, "第三方登录：统一通行证"),
        new(4, "第三方登录：Authlib Injector 或 LittleSkin")
    ];

    public IReadOnlyList<BoolOption> VersionIsolationOptions { get; } =
    [
        new(true, "开启"),
        new(false, "关闭")
    ];

    public IReadOnlyList<DisplayTypeOption> VersionDisplayTypeOptions { get; } =
    [
        new(MinecraftInstanceDisplayType.Auto, "自动"),
        new(MinecraftInstanceDisplayType.Hidden, "从版本列表中隐藏"),
        new(MinecraftInstanceDisplayType.Api, "可安装 Mod 的版本"),
        new(MinecraftInstanceDisplayType.OriginalLike, "常规版本"),
        new(MinecraftInstanceDisplayType.Rubbish, "不常用版本"),
        new(MinecraftInstanceDisplayType.Fool, "愚人节版本")
    ];

    public IReadOnlyList<IntOption> LocalModFilterOptions { get; } =
    [
        new(0, "全部"),
        new(1, "已启用"),
        new(2, "已禁用")
    ];

    public IReadOnlyList<InstanceDetailSectionOption> InstanceDetailSections { get; } =
    [
        new(0, "概览", "信息、文件夹、导出与维护"),
        new(1, "Mod", "本地 Mod 管理与更新"),
        new(2, "启动设置", "版本独立启动参数")
    ];

    public bool IsInstanceOverviewSectionSelected => SelectedInstanceDetailSection == 0;

    public bool IsInstanceModSectionSelected => SelectedInstanceDetailSection == 1;

    public bool IsInstanceLaunchSettingsSectionSelected => SelectedInstanceDetailSection == 2;

    public IReadOnlyList<IntOption> VersionSortOptions { get; } =
    [
        new(0, "按发布时间"),
        new(1, "按名称 A-Z"),
        new(2, "按名称 Z-A")
    ];

    public IAsyncRelayCommand RefreshCommand { get; private set; } = null!;

    public IRelayCommand BrowseMinecraftRootCommand { get; private set; } = null!;

    public IAsyncRelayCommand ImportInstanceCommand { get; private set; } = null!;

    public IRelayCommand RemoveMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand RenameMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand OpenMinecraftRootCommand { get; private set; } = null!;

    public IAsyncRelayCommand SaveInstanceLaunchSettingsCommand { get; private set; } = null!;

    public IAsyncRelayCommand ResetInstanceLaunchSettingsCommand { get; private set; } = null!;

    public IAsyncRelayCommand CompleteSelectedInstanceFilesCommand { get; private set; } = null!;

    public IRelayCommand BrowseJavaCommand { get; private set; } = null!;

    public IRelayCommand UseSelectedInstanceForLaunchCommand { get; private set; } = null!;

    public IRelayCommand<MinecraftInstance> UseInstanceForLaunchFromListCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedInstanceFolderCommand { get; private set; } = null!;

    public IRelayCommand<MinecraftInstance> OpenInstanceFolderFromListCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedSavesFolderCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedModsFolderCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedResourcePacksFolderCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedShaderPacksFolderCommand { get; private set; } = null!;

    public IRelayCommand OpenSelectedScreenshotsFolderCommand { get; private set; } = null!;

    public IAsyncRelayCommand RenameSelectedInstanceCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> RenameInstanceFromListCommand { get; private set; } = null!;

    public IAsyncRelayCommand CloneSelectedInstanceCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> CloneInstanceFromListCommand { get; private set; } = null!;

    public IAsyncRelayCommand ExportSelectedInstanceScriptCommand { get; private set; } = null!;

    public IAsyncRelayCommand ExportSelectedInstanceModpackCommand { get; private set; } = null!;

    public IAsyncRelayCommand ToggleSelectedInstanceStarCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> ToggleInstanceStarFromListCommand { get; private set; } = null!;

    public IAsyncRelayCommand ToggleSelectedInstanceHiddenCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> ToggleInstanceHiddenFromListCommand { get; private set; } = null!;

    public IAsyncRelayCommand DeleteSelectedInstanceCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> DeleteInstanceFromListCommand { get; private set; } = null!;

    public IRelayCommand<MinecraftInstance> SelectInstanceCommand { get; private set; } = null!;

    public IAsyncRelayCommand RefreshLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand CheckLocalModUpdatesCommand { get; private set; } = null!;

    public IAsyncRelayCommand UpdateSelectedLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand UpdateAllLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand InstallLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand DownloadModsForSelectedInstanceCommand { get; private set; } = null!;

    public IRelayCommand<LocalModListRow> ToggleLocalModSelectionCommand { get; private set; } = null!;

    public IRelayCommand SelectAllLocalModsCommand { get; private set; } = null!;

    public IRelayCommand ClearSelectedLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand EnableSelectedLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand DisableSelectedLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand DeleteSelectedLocalModsCommand { get; private set; } = null!;

    public IAsyncRelayCommand ToggleSelectedLocalModEnabledCommand { get; private set; } = null!;

    public IAsyncRelayCommand DeleteSelectedLocalModCommand { get; private set; } = null!;

    public int InstanceCount => Instances.Count;

    public int ErrorCount => Instances.Count(instance => instance.HasError);

    public string SelectedInstanceDetail => SelectedInstance is null
        ? "尚未选择实例"
        : BuildSelectedInstanceDetail(SelectedInstance);

    public string SelectedInstanceTechnicalDetail => SelectedInstance is null
        ? "请选择一个版本查看详细信息。"
        : BuildSelectedInstanceTechnicalDetail(SelectedInstance);

    public string SelectedInstanceOverview => SelectedInstance is null
        ? "请选择一个版本查看概览。"
        : BuildSelectedInstanceOverview(SelectedInstance);

    public string CurrentLaunchVersionName
    {
        get
        {
            var name = GetLaunchInstanceName();
            return string.IsNullOrWhiteSpace(name) ? "未选择" : name;
        }
    }

    public string VersionManagementSummary => SelectedInstance is null
        ? $"启动版本：{CurrentLaunchVersionName}"
        : $"正在管理：{SelectedInstance.Name} / 启动版本：{CurrentLaunchVersionName}";

    public bool IsSelectedInstanceLaunchVersion => SelectedInstance is not null
        && string.Equals(SelectedInstance.Name, GetLaunchInstanceName(), StringComparison.OrdinalIgnoreCase);

    public string SelectedLaunchActionText => IsSelectedInstanceLaunchVersion ? "已是启动版本" : "设为启动版本";

    public string InstanceLaunchSettingsTitle => SelectedInstance is null
        ? "实例启动设置"
        : $"{SelectedInstance.Name} 的启动设置";

    public string SelectedStarActionText => SelectedInstance?.IsStar == true ? "取消收藏" : "收藏";

    public string SelectedHiddenActionText => SelectedInstance?.IsHidden == true ? "取消隐藏" : "隐藏";

    public string InstanceLaunchOverrideSummary
    {
        get
        {
            if (SelectedInstance is null)
            {
                return "请选择一个版本查看实例单独设置。";
            }

            var count = CountSavedInstanceLaunchSettings(SelectedInstance.Name);
            return count == 0
                ? "当前版本未覆盖全局启动设置，将完全跟随全局配置。"
                : $"当前版本覆盖了 {count} 项启动设置；未覆盖的项目继续跟随全局配置。";
        }
    }

    public int HiddenCount => _allInstances.Count(instance => instance.IsHidden);

    public bool HasInstanceRows => InstanceRows.Any(row => row.IsSelectable);

    public int InstanceListVisibleCount => InstanceRows
        .Where(row => row.Instance is not null)
        .Select(row => row.Instance!.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public string InstanceListSummary
    {
        get
        {
            var total = ShowHiddenInstances
                ? _allInstances.Count(instance => instance.IsHidden)
                : _allInstances.Count(instance => !instance.IsHidden);
            var query = InstanceSearchText.Trim();
            var scope = ShowHiddenInstances ? "隐藏版本" : "可用版本";
            var search = string.IsNullOrWhiteSpace(query) ? "" : $"，搜索：{query}";
            return $"显示 {InstanceListVisibleCount} / {total} 个{scope}{search}，启动版本：{CurrentLaunchVersionName}";
        }
    }

    public string InstanceListEmptyText
    {
        get
        {
            if (_allInstances.Count == 0)
            {
                return "未找到任何本地版本，请先下载游戏或添加 .minecraft 文件夹。";
            }

            if (!string.IsNullOrWhiteSpace(InstanceSearchText))
            {
                return "没有匹配当前搜索条件的版本。";
            }

            return ShowHiddenInstances
                ? "当前没有隐藏版本。"
                : "当前没有可显示版本，可尝试勾选显示隐藏版本。";
        }
    }

    public int LocalModCount => LocalMods.Count;

    public int EnabledLocalModCount => _allLocalMods.Count(mod => mod.IsEnabled);

    public int DisabledLocalModCount => _allLocalMods.Count(mod => !mod.IsEnabled);

    public int UpdateLocalModCount => _localModUpdateInfos.Values.Count(info => info.HasUpdate);

    public int SelectedLocalModCount => _selectedLocalModKeys.Count;

    public int SelectedEnabledLocalModCount => GetSelectedLocalMods().Count(mod => mod.IsEnabled);

    public int SelectedDisabledLocalModCount => GetSelectedLocalMods().Count(mod => !mod.IsEnabled);

    public int SelectedUpdateLocalModCount => GetSelectedLocalMods().Count(HasLocalModUpdate);

    public bool HasSelectedLocalMods => SelectedLocalModCount > 0;

    public string LocalModSelectionSummary => HasSelectedLocalMods ? $"已选择 {SelectedLocalModCount} 个文件" : "未选择文件";

    public bool HasLocalMods => LocalMods.Count > 0;

    public string LocalModsDirectory => SelectedInstance is null ? "" : Path.Combine(ResolveSelectedGameDirectory(), "mods");

    public string SelectedLocalModActionText => SelectedLocalMod?.IsEnabled == true ? "禁用" : "启用";

    public bool IsVersionNideServerLogin => VersionServerLogin == 3;

    public bool IsVersionAuthServerLogin => VersionServerLogin == 4;

}

