using CommunityToolkit.Mvvm.ComponentModel;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public sealed class InstanceListRow : ObservableObject
{
    public InstanceListRow(string groupTitle)
    {
        IsHeader = true;
        GroupTitle = groupTitle;
    }

    public InstanceListRow(MinecraftInstance instance, string? launchInstanceName, string? managedInstanceName)
    {
        Instance = instance;
        GroupTitle = instance.GroupName;
        UpdateRoleNames(launchInstanceName, managedInstanceName);
    }

    public bool IsHeader { get; }

    public bool IsSelectable => Instance is not null;

    public MinecraftInstance? Instance { get; }

    private bool isLaunchVersion;

    public bool IsLaunchVersion
    {
        get => isLaunchVersion;
        private set => SetProperty(ref isLaunchVersion, value);
    }

    private bool isManagedVersion;

    public bool IsManagedVersion
    {
        get => isManagedVersion;
        private set => SetProperty(ref isManagedVersion, value);
    }

    public bool IsLaunchAndManagedVersion => IsLaunchVersion && IsManagedVersion;

    public string GroupTitle { get; }

    public string Name => Instance?.Name ?? GroupTitle;

    public string DisplayVersion => Instance?.DisplayVersion ?? "";

    public string DisplayInfo => Instance?.DisplayInfo ?? "";

    public string GroupName => Instance?.GroupName ?? "";

    public string IconPath => Instance?.IconPath ?? "";

    public string IconDescription => Instance?.IconDescription ?? "";

    public string StarText => Instance?.StarText ?? "";

    public string HiddenText => Instance?.HiddenText ?? "";

    public string StateText => Instance is null ? "" : Instance.State.ToString();

    public string LaunchText => IsLaunchVersion ? "启动版本" : "";

    public string ManagedText => IsManagedVersion ? "正在管理" : "";

    public string RoleText
    {
        get
        {
            if (IsLaunchAndManagedVersion)
            {
                return "启动并正在管理";
            }

            if (IsManagedVersion)
            {
                return "正在管理";
            }

            return IsLaunchVersion ? "启动版本" : "";
        }
    }

    public void UpdateRoleNames(string? launchInstanceName, string? managedInstanceName)
    {
        IsLaunchVersion = Instance is not null && string.Equals(Instance.Name, launchInstanceName, StringComparison.OrdinalIgnoreCase);
        IsManagedVersion = Instance is not null && string.Equals(Instance.Name, managedInstanceName, StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(IsLaunchAndManagedVersion));
        OnPropertyChanged(nameof(LaunchText));
        OnPropertyChanged(nameof(ManagedText));
        OnPropertyChanged(nameof(RoleText));
    }
}
