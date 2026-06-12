using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;


public sealed partial class DownloadPageViewModel
{
    public void BrowseMinecraftRoot()
    {
        var selected = _fileDialogs.PickFolder("选择 .minecraft 文件夹", MinecraftRootPath);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        try
        {
            var folder = _rootFolders.AddFolder(selected);
            MinecraftRootPath = folder.Path;
            UpdateResourceInstallTargetLabel();
            RefreshMinecraftRootFolders();
            StatusMessage = "已添加 Minecraft 文件夹：" + folder.Name;
        }
        catch (Exception ex)
        {
            StatusMessage = "添加 Minecraft 文件夹失败：" + ex.Message;
            _logger.Error(ex, "添加 Minecraft 文件夹失败");
        }
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
        UpdateResourceInstallTargetLabel();
        RefreshMinecraftRootFolders();
        StatusMessage = "Minecraft 文件夹已从列表中移除";
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
        _ = _settings.SaveAsync();
    }

    private void OpenSelectedMinecraftRoot()
    {
        if (SelectedMinecraftRootFolder is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SelectedMinecraftRootFolder.Path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedMinecraftRootFolder.Path,
                UseShellExecute = true
            });
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

    private void SyncMinecraftRootPathFromSettings()
    {
        var savedRoot = _settings.Get(AppSettingKeys.MinecraftRootPath, "");
        var targetRoot = string.IsNullOrWhiteSpace(savedRoot) ? _minecraftDiscovery.GetDefaultMinecraftRoot() : savedRoot;
        if (!string.Equals(MinecraftRootPath, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            MinecraftRootPath = targetRoot;
            return;
        }

        RefreshMinecraftRootFolders();
        UpdateResourceInstallTargetLabel();
    }
}
