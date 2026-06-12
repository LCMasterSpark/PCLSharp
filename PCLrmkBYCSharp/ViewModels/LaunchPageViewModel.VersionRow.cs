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
    public sealed class LaunchVersionListRow : ObservableObject
    {
        private bool _isCurrentLaunchVersion;

        public LaunchVersionListRow(string groupTitle)
        {
            IsHeader = true;
            GroupTitle = groupTitle;
        }

        public LaunchVersionListRow(MinecraftInstance instance, string? selectedInstanceName)
        {
            Instance = instance;
            GroupTitle = instance.GroupName;
            _isCurrentLaunchVersion = string.Equals(instance.Name, selectedInstanceName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsHeader { get; }

        public bool IsSelectable => Instance is not null;

        public MinecraftInstance? Instance { get; }

        public bool IsCurrentLaunchVersion
        {
            get => _isCurrentLaunchVersion;
            private set
            {
                if (SetProperty(ref _isCurrentLaunchVersion, value))
                {
                    OnPropertyChanged(nameof(CurrentLaunchText));
                }
            }
        }

        public void RefreshCurrentLaunchVersion(string? selectedInstanceName)
        {
            IsCurrentLaunchVersion = Instance is not null
                && string.Equals(Instance.Name, selectedInstanceName, StringComparison.OrdinalIgnoreCase);
        }

        public string GroupTitle { get; }

        public string Name => Instance?.Name ?? GroupTitle;

        public string DisplayVersion => Instance?.DisplayVersion ?? "";

        public string DisplayInfo => Instance?.DisplayInfo ?? "";

        public string LoaderSummary => Instance?.LoaderSummary ?? "";

        public string StarText => Instance?.StarText ?? "";

        public string StarActionText => Instance?.IsStar == true ? "取消收藏" : "收藏";

        public string HiddenText => Instance?.HiddenText ?? "";

        public string HiddenActionText => Instance?.IsHidden == true ? "取消隐藏" : "隐藏";

        public string StateText => Instance is null ? "" : Instance.State.ToString();

        public string CurrentLaunchText => IsCurrentLaunchVersion ? "当前启动" : "";

        public string IconPath => Instance?.IconPath ?? "";

        public string IconDescription => Instance?.IconDescription ?? "";
    }

}
