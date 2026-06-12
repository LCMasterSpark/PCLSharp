using CommunityToolkit.Mvvm.ComponentModel;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class InstancePageViewModel
{
    [ObservableProperty]
    private string minecraftRootPath;

    [ObservableProperty]
    private MinecraftRootFolder? selectedMinecraftRootFolder;

    [ObservableProperty]
    private MinecraftInstance? selectedInstance;

    [ObservableProperty]
    private InstanceListRow? selectedInstanceRow;

    [ObservableProperty]
    private int minMemoryMb;

    [ObservableProperty]
    private int maxMemoryMb;

    [ObservableProperty]
    private int versionRamType;

    [ObservableProperty]
    private int versionRamCustom;

    [ObservableProperty]
    private int launchWindowWidth;

    [ObservableProperty]
    private int launchWindowHeight;

    [ObservableProperty]
    private string versionArgumentTitle = string.Empty;

    [ObservableProperty]
    private string versionCustomInfo = string.Empty;

    [ObservableProperty]
    private string extraJvmArgs = string.Empty;

    [ObservableProperty]
    private string extraGameArgs = string.Empty;

    [ObservableProperty]
    private string versionAdvanceRun = string.Empty;

    [ObservableProperty]
    private bool versionAdvanceRunWait;

    [ObservableProperty]
    private string versionJavaPath = string.Empty;

    [ObservableProperty]
    private string serverIp = string.Empty;

    [ObservableProperty]
    private int versionServerLogin;

    [ObservableProperty]
    private string versionServerNide = string.Empty;

    [ObservableProperty]
    private string versionServerAuthServer = string.Empty;

    [ObservableProperty]
    private string versionServerAuthRegister = string.Empty;

    [ObservableProperty]
    private string versionServerAuthName = string.Empty;

    [ObservableProperty]
    private int versionGc;

    [ObservableProperty]
    private int versionRamOptimize;

    [ObservableProperty]
    private MinecraftInstanceDisplayType versionDisplayType;

    [ObservableProperty]
    private bool disableJlw;

    [ObservableProperty]
    private bool disableLua;

    [ObservableProperty]
    private bool disableModUpdate;

    [ObservableProperty]
    private bool ignoreJavaCompatibility;

    [ObservableProperty]
    private bool disableFileCheck;

    [ObservableProperty]
    private bool versionIsolationEnabled;

    [ObservableProperty]
    private string gameDirectoryPath = string.Empty;

    [ObservableProperty]
    private string exportPackName = string.Empty;

    [ObservableProperty]
    private string exportPackVersion = "1.0.0";

    [ObservableProperty]
    private bool exportIncludeConfig = true;

    [ObservableProperty]
    private bool exportIncludeMods = true;

    [ObservableProperty]
    private bool exportIncludeResourcePacks = true;

    [ObservableProperty]
    private bool exportIncludeShaderPacks = true;

    [ObservableProperty]
    private bool exportIncludeSaves = true;

    [ObservableProperty]
    private bool exportIncludeScreenshots;

    [ObservableProperty]
    private bool exportIncludeOptions = true;

    [ObservableProperty]
    private bool exportIncludeExtraData = true;

    [ObservableProperty]
    private string statusMessage = "等待扫描";

    [ObservableProperty]
    private string fileCompletionSummary = "尚未执行文件补全";

    [ObservableProperty]
    private string fileCompletionTaskName = "";

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private bool isCompletingFiles;

    [ObservableProperty]
    private bool showHiddenInstances;

    [ObservableProperty]
    private string instanceSearchText = string.Empty;

    [ObservableProperty]
    private int versionSortMode;

    [ObservableProperty]
    private LocalModListRow? selectedLocalMod;

    [ObservableProperty]
    private string localModSearchText = string.Empty;

    [ObservableProperty]
    private int localModFilter;

    [ObservableProperty]
    private bool isScanningLocalMods;

    [ObservableProperty]
    private bool isCheckingLocalModUpdates;

    [ObservableProperty]
    private int selectedInstanceDetailSection;

}
