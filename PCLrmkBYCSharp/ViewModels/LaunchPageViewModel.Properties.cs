using CommunityToolkit.Mvvm.ComponentModel;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class LaunchPageViewModel
{
    [ObservableProperty]
    private string minecraftRootPath;

    [ObservableProperty]
    private MinecraftRootFolder? selectedMinecraftRootFolder;

    [ObservableProperty]
    private MinecraftInstance? selectedInstance;

    [ObservableProperty]
    private LaunchVersionListRow? selectedVersionSelectorRow;

    [ObservableProperty]
    private JavaEntry? selectedJava;

    [ObservableProperty]
    private JavaEntryOption? selectedJavaOption;

    [ObservableProperty]
    private LoginType selectedLoginType;

    [ObservableProperty]
    private string legacyName;

    [ObservableProperty]
    private int launchSkinType;

    [ObservableProperty]
    private string launchSkinId = string.Empty;

    [ObservableProperty]
    private bool launchSkinSlim;

    [ObservableProperty]
    private string loginUserName;

    [ObservableProperty]
    private string loginPassword;

    [ObservableProperty]
    private string loginServer;

    [ObservableProperty]
    private string microsoftClientId;

    [ObservableProperty]
    private bool isMicrosoftClientIdEditorVisible;

    [ObservableProperty]
    private MicrosoftAccountCacheEntry? selectedMicrosoftAccount;

    [ObservableProperty]
    private bool rememberLogin;

    [ObservableProperty]
    private int launcherVisibility;

    [ObservableProperty]
    private int minMemoryMb;

    [ObservableProperty]
    private int maxMemoryMb;

    [ObservableProperty]
    private int launchWindowWidth;

    [ObservableProperty]
    private int launchWindowHeight;

    [ObservableProperty]
    private int launchWindowType;

    [ObservableProperty]
    private string extraJvmArgs = string.Empty;

    [ObservableProperty]
    private string extraGameArgs = string.Empty;

    [ObservableProperty]
    private string serverIp = string.Empty;

    [ObservableProperty]
    private string statusMessage = "启动页已就绪";

    [ObservableProperty]
    private string commandPreview = "启动后会在这里显示脱敏后的命令摘要";

    [ObservableProperty]
    private string launchDiagnostics = string.Empty;

    [ObservableProperty]
    private bool hasLaunchFileCompletionAction;

    [ObservableProperty]
    private string fileCompletionSummary = "等待启动前文件检查";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isVersionSelectorOpen;

    [ObservableProperty]
    private string versionSearchText = string.Empty;

    [ObservableProperty]
    private bool showHiddenVersions;

    [ObservableProperty]
    private int versionSortMode;

    [ObservableProperty]
    private bool isMicrosoftDeviceCodeActive;

    [ObservableProperty]
    private string microsoftDeviceCode = string.Empty;

    [ObservableProperty]
    private string microsoftDeviceCodeVerificationUri = string.Empty;

    [ObservableProperty]
    private string microsoftDeviceCodeMessage = string.Empty;

    [ObservableProperty]
    private string microsoftDeviceCodeExpiresText = string.Empty;

}
