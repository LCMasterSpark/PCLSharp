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
    private async Task ScanJavaCoreAsync()
    {
        var selectedVersionPath = SelectedInstance?.VersionPath;
        var javaEntries = await _javaDiscovery.DiscoverAsync(MinecraftRootPath, selectedVersionPath);
        await InvokeOnUiAsync(() => ApplyDiscoveredJavaEntries(javaEntries));
    }

    private void ApplyDiscoveredJavaEntries(IReadOnlyList<JavaEntry> javaEntries)
    {
        JavaEntries.Clear();
        foreach (var java in javaEntries)
        {
            JavaEntries.Add(java);
        }

        var savedJava = ResolveJavaPath(SelectedInstance?.Name);
        _isRestoringJavaSelection = true;
        try
        {
            SelectedJava = JavaEntries.FirstOrDefault(java => string.Equals(java.PathJava, savedJava, StringComparison.OrdinalIgnoreCase))
                ?? JavaEntries.FirstOrDefault();
        }
        finally
        {
            _isRestoringJavaSelection = false;
        }

        OnPropertyChanged(nameof(SelectedJavaSummary));
        StatusMessage = $"Java 扫描完成：{JavaEntries.Count} 个";
    }

    private void BrowseMinecraftRoot()
    {
        var selected = _fileDialogs.PickFolder("选择 Minecraft 根目录", MinecraftRootPath);
        if (selected is null)
        {
            return;
        }

        try
        {
            var folder = _rootFolders.AddFolder(selected);
            MinecraftRootPath = folder.Path;
            RefreshMinecraftRootFolders();
        }
        catch (Exception ex)
        {
            StatusMessage = "添加 Minecraft 文件夹失败：" + ex.Message;
            _logger.Error(ex, "添加 Minecraft 文件夹失败");
            return;
        }

        _isInitialized = false;
        _ = InitializeAsync();
    }

    private void RemoveSelectedMinecraftRoot()
    {
        if (SelectedMinecraftRootFolder is null || SelectedMinecraftRootFolder.Type == MinecraftRootFolderType.Vanilla)
        {
            return;
        }

        _rootFolders.RemoveFolder(SelectedMinecraftRootFolder.Path);
        var next = MinecraftRootFolders.FirstOrDefault(folder => !string.Equals(folder.Path, SelectedMinecraftRootFolder.Path, StringComparison.OrdinalIgnoreCase))
            ?? _rootFolders.LoadFolders(_minecraftDiscovery.GetDefaultMinecraftRoot(), "").FirstOrDefault();
        MinecraftRootPath = next?.Path ?? _minecraftDiscovery.GetDefaultMinecraftRoot();
        RefreshMinecraftRootFolders();
        _isInitialized = false;
        _ = InitializeAsync();
    }

    private void RenameSelectedMinecraftRoot()
    {
        if (SelectedMinecraftRootFolder is null)
        {
            return;
        }

        var newName = _prompts.Prompt("重命名文件夹", "请输入该 Minecraft 文件夹在列表中的显示名称。", SelectedMinecraftRootFolder.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        _rootFolders.RenameFolder(SelectedMinecraftRootFolder.Path, newName);
        RefreshMinecraftRootFolders();
        StatusMessage = "Minecraft 文件夹名称已更新为 " + newName.Trim();
        _ = SaveSettingsAsync();
    }

    private void OpenSelectedMinecraftRoot()
    {
        if (SelectedMinecraftRootFolder is null)
        {
            return;
        }

        try
        {
            _folders.OpenFolder(SelectedMinecraftRootFolder.Path);
            StatusMessage = "已打开 Minecraft 文件夹";
        }
        catch (Exception ex)
        {
            StatusMessage = "打开 Minecraft 文件夹失败：" + ex.Message;
            _logger.Error(ex, "打开 Minecraft 文件夹失败");
        }
    }

    private void RefreshMinecraftRootFolders()
    {
        var folders = _rootFolders.LoadFolders(_minecraftDiscovery.GetDefaultMinecraftRoot(), MinecraftRootPath);
        MinecraftRootFolders.Clear();
        foreach (var folder in folders)
        {
            MinecraftRootFolders.Add(folder);
        }

        _isSyncingRootFolderSelection = true;
        try
        {
            SelectedMinecraftRootFolder = MinecraftRootFolders.FirstOrDefault(folder => string.Equals(folder.Path, MinecraftRootPath, StringComparison.OrdinalIgnoreCase))
                ?? MinecraftRootFolders.FirstOrDefault();
        }
        finally
        {
            _isSyncingRootFolderSelection = false;
        }

        RemoveMinecraftRootCommand?.NotifyCanExecuteChanged();
        RenameMinecraftRootCommand?.NotifyCanExecuteChanged();
        OpenMinecraftRootCommand?.NotifyCanExecuteChanged();
    }

    private async Task BrowseJavaAsync()
    {
        var selected = _fileDialogs.PickJavaExecutable(SelectedJava?.PathFolder ?? "");
        if (selected is null)
        {
            return;
        }

        var imported = await _javaDiscovery.InspectJavaAsync(selected, isUserImport: true);
        if (imported is null)
        {
            StatusMessage = "选择的 java.exe 无法识别";
            return;
        }

        if (!JavaEntries.Any(java => string.Equals(java.PathJava, imported.PathJava, StringComparison.OrdinalIgnoreCase)))
        {
            JavaEntries.Insert(0, imported);
        }

        SelectedJava = imported;
        _settings.Set(AppSettingKeys.LaunchArgumentJavaSelect, imported.ToPclSettingJson());
        await _settings.SaveAsync();
    }

    private void BrowseLegacySkin()
    {
        var initialDirectory = File.Exists(LaunchSkinId)
            ? Path.GetDirectoryName(LaunchSkinId) ?? ""
            : "";
        var selected = _fileDialogs.PickSkinFile(initialDirectory);
        if (selected is null)
        {
            return;
        }

        LaunchSkinType = 4;
        LaunchSkinId = selected;
    }

}
