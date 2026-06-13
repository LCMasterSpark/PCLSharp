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
    private void EnableCollectionSynchronization()
    {
        BindingOperations.EnableCollectionSynchronization(Instances, _instancesSync);
        BindingOperations.EnableCollectionSynchronization(MinecraftRootFolders, _minecraftRootFoldersSync);
        BindingOperations.EnableCollectionSynchronization(VersionSelectorRows, _versionSelectorRowsSync);
        BindingOperations.EnableCollectionSynchronization(JavaEntries, _javaEntriesSync);
        BindingOperations.EnableCollectionSynchronization(JavaEntryOptions, _javaEntryOptionsSync);
        BindingOperations.EnableCollectionSynchronization(Steps, _stepsSync);
        BindingOperations.EnableCollectionSynchronization(FileCompletionDetails, _fileCompletionDetailsSync);
        BindingOperations.EnableCollectionSynchronization(MicrosoftAccounts, _microsoftAccountsSync);
    }

    public ObservableCollection<MinecraftInstance> Instances { get; } = [];

    public ObservableCollection<MinecraftRootFolder> MinecraftRootFolders { get; } = [];

    public ObservableCollection<LaunchVersionListRow> VersionSelectorRows { get; } = [];

    public ObservableCollection<JavaEntry> JavaEntries { get; } = [];

    public ObservableCollection<JavaEntryOption> JavaEntryOptions { get; } = [];

    public ObservableCollection<LaunchStepState> Steps { get; } = [];

    public ObservableCollection<string> FileCompletionDetails { get; } = [];

    public ObservableCollection<MicrosoftAccountCacheEntry> MicrosoftAccounts { get; } = [];

    public IReadOnlyList<LoginTypeOption> LoginTypeOptions { get; } =
    [
        new(LoginType.Ms, "正版登录"),
        new(LoginType.Legacy, "离线登录"),
        new(LoginType.Nide, "统一通行证"),
        new(LoginType.Auth, "Authlib-Injector")
    ];

    public IReadOnlyList<IntOption> LegacySkinTypeOptions { get; } =
    [
        new(0, "默认"),
        new(1, "Steve"),
        new(2, "Alex"),
        new(3, "使用正版用户名"),
        new(4, "自定义皮肤")
    ];

    public IReadOnlyList<IntOption> WindowTypeOptions { get; } =
    [
        new(0, "全屏"),
        new(1, "默认"),
        new(2, "与启动器尺寸一致"),
        new(3, "自定义尺寸"),
        new(4, "最大化")
    ];

    public IReadOnlyList<IntOption> LauncherVisibilityOptions { get; } =
    [
        new(0, "启动后立刻关闭"),
        new(2, "启动后隐藏，退出后自动关闭"),
        new(3, "启动后隐藏，退出后重新打开"),
        new(4, "启动后最小化"),
        new(5, "启动后保持不变")
    ];

    public IReadOnlyList<IntOption> VersionSortOptions { get; } =
    [
        new(0, "按发布时间"),
        new(1, "按名称 A-Z"),
        new(2, "按名称 Z-A")
    ];

    public IAsyncRelayCommand InitializeCommand { get; private set; } = null!;

    public IAsyncRelayCommand RefreshInstancesCommand { get; private set; } = null!;

    public IAsyncRelayCommand ScanJavaCommand { get; private set; } = null!;

    public IRelayCommand BrowseMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand RemoveMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand RenameMinecraftRootCommand { get; private set; } = null!;

    public IRelayCommand OpenMinecraftRootCommand { get; private set; } = null!;

    public IAsyncRelayCommand BrowseJavaCommand { get; private set; } = null!;

    public IRelayCommand BrowseLegacySkinCommand { get; private set; } = null!;

    public IAsyncRelayCommand GenerateProfileCommand { get; private set; } = null!;

    public IAsyncRelayCommand ExportLaunchScriptCommand { get; private set; } = null!;

    public IAsyncRelayCommand LaunchGameCommand { get; private set; } = null!;

    public IRelayCommand CancelBusyCommand { get; private set; } = null!;

    public IAsyncRelayCommand LoginMicrosoftAccountCommand { get; private set; } = null!;

    public IAsyncRelayCommand RefreshMicrosoftAccountCommand { get; private set; } = null!;

    public IAsyncRelayCommand LogoutMicrosoftAccountCommand { get; private set; } = null!;

    public IRelayCommand ToggleMicrosoftClientIdEditorCommand { get; private set; } = null!;

    public IRelayCommand SwitchMicrosoftAccountCommand { get; private set; } = null!;

    public IRelayCommand DeleteMicrosoftAccountCommand { get; private set; } = null!;

    public IAsyncRelayCommand LoginServerAccountCommand { get; private set; } = null!;

    public IAsyncRelayCommand LogoutServerAccountCommand { get; private set; } = null!;

    public IRelayCommand OpenMicrosoftDeviceCodePageCommand { get; private set; } = null!;

    public IRelayCommand CopyMicrosoftDeviceCodeCommand { get; private set; } = null!;

    public IRelayCommand OpenVersionSelectorCommand { get; private set; } = null!;

    public IRelayCommand CloseVersionSelectorCommand { get; private set; } = null!;

    public IRelayCommand<MinecraftInstance> SelectVersionCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> ToggleVersionStarCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> ToggleVersionHiddenCommand { get; private set; } = null!;

    public IAsyncRelayCommand<MinecraftInstance> DeleteVersionCommand { get; private set; } = null!;

    public IRelayCommand<MinecraftInstance> OpenVersionFolderCommand { get; private set; } = null!;

    public string SelectedInstanceSummary => SelectedInstance is null
        ? "未选择实例"
        : BuildSelectedInstanceSummary(SelectedInstance);

    public string CurrentVersionTitle => SelectedInstance?.Name ?? "未找到可用的游戏版本";

    public string CurrentVersionSubtitle => SelectedInstance is null
        ? "请先下载游戏，或在版本选择中切换 Minecraft 文件夹"
        : SelectedInstance.DisplayVersion;

    public string SelectedJavaSummary => SelectedJava is null
        ? "未选择 Java"
        : $"{SelectedJava.DisplayName}: {SelectedJava.PathJava}";

    public string InstanceLaunchSettingsSummary => SelectedInstance is null
        ? "实例启动设置会在实例页中按旧版键名单独保存"
        : $"当前实例设置：{MinMemoryMb}-{MaxMemoryMb} MB，窗口 {LaunchWindowWidth}x{LaunchWindowHeight}";

    public string LaunchCurrentStepText
    {
        get
        {
            var running = Steps.FirstOrDefault(step => step.Status == LaunchStepStatus.Running);
            if (running is not null)
            {
                return running.Name;
            }

            var failed = Steps.FirstOrDefault(step => step.Status == LaunchStepStatus.Failed);
            if (failed is not null)
            {
                return failed.Name;
            }

            return Steps.LastOrDefault(step => step.Status is LaunchStepStatus.Succeeded or LaunchStepStatus.Skipped)?.Name ?? "初始化";
        }
    }

    public double LaunchProgressPercent
    {
        get
        {
            if (Steps.Count == 0)
            {
                return IsBusy ? 5 : 0;
            }

            var finished = Steps.Count(step => step.Status is LaunchStepStatus.Succeeded or LaunchStepStatus.Skipped);
            var running = Steps.Any(step => step.Status == LaunchStepStatus.Running) ? 0.5 : 0;
            return Math.Clamp((finished + running) / Steps.Count * 100, 0, 100);
        }
    }

    public string LaunchProgressText => $"{LaunchProgressPercent:0.00} %";

    public bool HasLaunchDiagnostics => !string.IsNullOrWhiteSpace(LaunchDiagnostics);

    public bool HasInstances => Instances.Count > 0;

    public bool HasSelectedInstance => SelectedInstance is not null;

    public bool HasNoSelectedInstance => SelectedInstance is null;

    public bool HasVersionSelectorRows => VersionSelectorRows.Any(row => row.IsSelectable);

    public int VersionSelectorVisibleCount => VersionSelectorRows
        .Where(row => row.Instance is not null)
        .Select(row => row.Instance!.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public string VersionSelectorSummary
    {
        get
        {
            var total = ShowHiddenVersions
                ? _allInstances.Count(instance => instance.IsHidden)
                : _allInstances.Count(instance => !instance.IsHidden);
            var query = VersionSearchText.Trim();
            var scope = ShowHiddenVersions ? "隐藏版本" : "可用版本";
            var search = string.IsNullOrWhiteSpace(query) ? "" : $"，搜索：{query}";
            return $"显示 {VersionSelectorVisibleCount} / {total} 个{scope}{search}，当前启动：{CurrentVersionTitle}";
        }
    }

    public string VersionSelectorEmptyText
    {
        get
        {
            if (_allInstances.Count == 0)
            {
                return "未找到任何本地版本，请先下载游戏或添加 .minecraft 文件夹。";
            }

            if (!string.IsNullOrWhiteSpace(VersionSearchText))
            {
                return "没有匹配当前搜索条件的版本。";
            }

            return ShowHiddenVersions
                ? "当前没有隐藏版本。"
                : "当前没有可显示版本，可尝试勾选显示隐藏版本。";
        }
    }

    public bool IsLegacyLogin => SelectedLoginType == LoginType.Legacy;

    public bool IsLegacySkinIdVisible => IsLegacyLogin && LaunchSkinType is 3 or 4;

    public bool IsLegacySkinBrowseVisible => IsLegacyLogin && LaunchSkinType == 4;

    public bool IsLegacySkinSlimVisible => IsLegacyLogin && LaunchSkinType == 4;

    public string LegacySkinIdLabel => LaunchSkinType == 3 ? "正版皮肤用户名" : "自定义皮肤文件";

    public string LegacySkinSummary => LaunchSkinType switch
    {
        1 => "离线 UUID 会调整为 Steve 模型。",
        2 => "离线 UUID 会调整为 Alex 模型。",
        3 => string.IsNullOrWhiteSpace(LaunchSkinId)
            ? "请输入正版玩家名，启动时将尝试使用该玩家 UUID。"
            : "启动时将尝试使用正版玩家 " + LaunchSkinId.Trim() + " 的 UUID。",
        4 => string.IsNullOrWhiteSpace(LaunchSkinId)
            ? "请选择本地 PNG 皮肤文件。"
            : "将使用本地皮肤文件：" + LaunchSkinId,
        _ => "使用默认离线 UUID 与皮肤模型。"
    };

    public bool IsMicrosoftLogin => SelectedLoginType == LoginType.Ms;

    public bool IsServerLogin => SelectedLoginType is LoginType.Nide or LoginType.Auth;

    public bool HasMicrosoftClientId => !string.IsNullOrWhiteSpace(MicrosoftClientId)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"));

    public bool HasMicrosoftAccount => !string.IsNullOrWhiteSpace(_settings.Get(AppSettingKeys.CacheMsV2OAuthRefresh, ""))
        || !string.IsNullOrWhiteSpace(_settings.Get(AppSettingKeys.CacheMsV2Access, ""))
        || !string.IsNullOrWhiteSpace(GetCachedMicrosoftProfile().Uuid);

    public bool HasValidMicrosoftAccessToken => !string.IsNullOrWhiteSpace(_settings.Get(AppSettingKeys.CacheMsV2Access, ""))
        && !string.IsNullOrWhiteSpace(GetCachedMicrosoftProfile().Uuid)
        && _settings.Get<long>(AppSettingKeys.CacheMsV2Expires, 0) > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

    public bool MicrosoftCacheNeedsRefresh => HasMicrosoftAccount && !HasValidMicrosoftAccessToken;

    public bool CanStartMicrosoftLogin => IsMicrosoftLogin && HasMicrosoftClientId && !IsBusy && _loginService is not null;

    public bool CanRefreshMicrosoftLogin => IsMicrosoftLogin && HasMicrosoftClientId && HasMicrosoftAccount && !IsBusy && _loginService is not null;

    public string MicrosoftLoginUnavailableReason
    {
        get
        {
            if (!IsMicrosoftLogin)
            {
                return "请先将登录方式切换为正版登录。";
            }

            if (_loginService is null)
            {
                return "正版登录服务尚未初始化。";
            }

            if (IsBusy)
            {
                return "当前正在执行启动页任务，请稍后再登录。";
            }

            if (!HasMicrosoftClientId)
            {
                return "正版网页登录需要 Microsoft Client ID。开源版不会内置 Client ID，请在此填写或设置环境变量 PCL_MS_CLIENT_ID。";
            }

            return "";
        }
    }

    public string MicrosoftRefreshUnavailableReason
    {
        get
        {
            var loginReason = MicrosoftLoginUnavailableReason;
            if (!string.IsNullOrWhiteSpace(loginReason))
            {
                return loginReason;
            }

            return HasMicrosoftAccount ? "" : "尚未缓存正版账号，请先完成网页登录。";
        }
    }

    public string MicrosoftLoginActionText => HasMicrosoftAccount ? "网页登录 / 换号" : "登录正版账号";

    public bool HasMicrosoftAccountHistory => MicrosoftAccounts.Count > 0;

    public string MicrosoftAccountHistorySummary => HasMicrosoftAccountHistory
        ? $"已缓存 {MicrosoftAccounts.Count} 个正版账号"
        : "尚未缓存正版账号，登录成功后会显示在这里。";

    public string MicrosoftClientIdHelp => HasMicrosoftClientId
        ? "Client ID 已配置。没有有效缓存时会打开微软网页授权。"
        : "正版登录需要 Microsoft Client ID；可在这里填写，或设置环境变量 PCL_MS_CLIENT_ID。";

    public string MicrosoftClientIdEditorActionText => IsMicrosoftClientIdEditorVisible ? "收起高级配置" : "配置 Client ID";

    public bool HasServerAccount => TryGetServerCachePrefix(out var prefix)
        && (!string.IsNullOrWhiteSpace(_settings.Get("Cache" + prefix + "Access", ""))
            || !string.IsNullOrWhiteSpace(_settings.Get("Cache" + prefix + "Client", ""))
            || !string.IsNullOrWhiteSpace(_settings.Get("Cache" + prefix + "Uuid", "")));

    public string MicrosoftAccountSummary
    {
        get
        {
            var (name, uuid) = GetCachedMicrosoftProfile();
            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(uuid))
            {
                var accountText = string.IsNullOrWhiteSpace(uuid)
                    ? "当前正版账号：" + name
                    : $"当前正版账号：{name} / {uuid}";
                return HasValidMicrosoftAccessToken
                    ? accountText + "，缓存仍有效。"
                    : accountText + "，下次登录会刷新授权。";
            }

            if (!HasMicrosoftClientId)
            {
                return "尚未配置 Microsoft Client ID，填写后才能进行正版登录。";
            }

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(uuid))
            {
                return "尚未登录正版账号，启动时会打开网页登录。";
            }

            return string.IsNullOrWhiteSpace(uuid)
                ? "当前正版账号：" + name
                : $"当前正版账号：{name} / {uuid}";
        }
    }

    public string MicrosoftReadinessSummary
    {
        get
        {
            if (!IsMicrosoftLogin)
            {
                return "";
            }

            if (HasValidMicrosoftAccessToken)
            {
                return "正版登录已就绪，启动时会使用当前缓存授权。";
            }

            if (HasMicrosoftAccount && HasMicrosoftClientId)
            {
                return "已有正版账号缓存，启动或刷新授权时会自动更新授权。";
            }

            if (HasMicrosoftAccount)
            {
                return "已有正版账号缓存，但授权需要刷新；请先填写 Microsoft Client ID。";
            }

            if (!HasMicrosoftClientId)
            {
                return "尚未配置 Microsoft Client ID，无法开始正版网页登录。";
            }

            return "尚未登录正版账号，请点击登录正版账号完成授权。";
        }
    }

    public string ServerAccountSummary
    {
        get
        {
            if (!TryGetServerCachePrefix(out var prefix))
            {
                return "";
            }

            var displayName = SelectedLoginType == LoginType.Nide ? "统一通行证" : "Authlib-Injector";
            var name = _settings.Get("Cache" + prefix + "Name", "");
            var uuid = _settings.Get("Cache" + prefix + "Uuid", "");
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(uuid))
            {
                return $"尚未登录{displayName}账号，启动时会验证账号密码。";
            }

            return string.IsNullOrWhiteSpace(uuid)
                ? $"当前{displayName}账号：" + name
                : $"当前{displayName}账号：{name} / {uuid}";
        }
    }

    public string SelectedLoginTypeDisplayName => LoginTypeOptions.FirstOrDefault(option => option.Value == SelectedLoginType)?.DisplayName ?? SelectedLoginType.ToString();

    public IReadOnlyList<string> LegacyNameHistory => GetHistory(AppSettingKeys.LoginLegacyName);

    public IReadOnlyList<string> LoginUserNameHistory => SelectedLoginType switch
    {
        LoginType.Nide => GetHistory(AppSettingKeys.LoginNideEmail),
        LoginType.Auth => GetHistory(AppSettingKeys.LoginAuthEmail),
        _ => []
    };
}

