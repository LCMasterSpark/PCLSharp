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

        _ = RefreshAsync();
    }

    private async Task ImportInstanceAsync()
    {
        var selected = _fileDialogs.PickFolder("选择要导入的版本文件夹", MinecraftRootPath);
        if (selected is null)
        {
            StatusMessage = "已取消导入版本";
            return;
        }

        try
        {
            var importedPath = _instanceManagement.ImportInstance(selected, MinecraftRootPath);
            var importedName = Path.GetFileName(importedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            _settings.Set(AppSettingKeys.InstanceManageSelectedName, importedName);
            var message = $"版本已导入：{importedName}，正在管理 {importedName}";
            StatusMessage = message;
            _logger.Info(StatusMessage);
            await RefreshAsync();
            SelectedInstance = Instances.FirstOrDefault(instance => string.Equals(instance.Name, importedName, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = "导入版本失败：" + ex.Message;
            _logger.Error(ex, "导入版本失败");
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
        RefreshMinecraftRootFolders();
        _ = RefreshAsync();
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

        OpenFolder(SelectedMinecraftRootFolder.Path, "Minecraft 文件夹");
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

    private void BrowseJava()
    {
        var selected = _fileDialogs.PickJavaExecutable(VersionJavaPath);
        if (selected is not null)
        {
            VersionJavaPath = selected;
        }
    }

    private void UseSelectedInstanceForLaunch()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        _settings.Set(AppSettingKeys.SelectedInstanceName, SelectedInstance.Name);
        _selections.WriteSelectedInstanceName(MinecraftRootPath, SelectedInstance.Name);
        UpdateInstanceRowRoles();
        OnPropertyChanged(nameof(SelectedInstanceOverview));
        StatusMessage = $"{SelectedInstance.Name} 已设为启动版本";
        _ = SaveSettingsAsync();
    }

    private void UseInstanceForLaunchFromList(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        UseSelectedInstanceForLaunch();
    }

    private void OpenInstanceFolderFromList(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        OpenSelectedInstanceFolder();
    }

    private async Task ToggleInstanceStarFromListAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        await ToggleSelectedInstanceStarAsync();
    }

    private async Task ToggleInstanceHiddenFromListAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        await ToggleSelectedInstanceHiddenAsync();
    }

    private void SelectInstance(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
    }

    private void OpenSelectedInstanceFolder()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        OpenFolder(SelectedInstance.VersionPath, "版本文件夹");
    }

    private void OpenSelectedSavesFolder()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        OpenFolder(Path.Combine(ResolveSelectedGameDirectory(), "saves"), "存档文件夹");
    }

    private void OpenSelectedModsFolder()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        OpenFolder(Path.Combine(ResolveSelectedGameDirectory(), "mods"), "Mod 文件夹");
    }

    private void OpenSelectedResourcePacksFolder()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请选择一个版本";
            return;
        }

        OpenFolder(Path.Combine(ResolveSelectedGameDirectory(), "resourcepacks"), "资源包文件夹");
    }

    private void OpenSelectedShaderPacksFolder()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请选择一个版本";
            return;
        }

        OpenFolder(Path.Combine(ResolveSelectedGameDirectory(), "shaderpacks"), "光影包文件夹");
    }

    private void OpenSelectedScreenshotsFolder()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请选择一个版本";
            return;
        }

        OpenFolder(Path.Combine(ResolveSelectedGameDirectory(), "screenshots"), "截图文件夹");
    }

    private void OpenFolder(string path, string displayName)
    {
        try
        {
            _folders.OpenFolder(path);
            StatusMessage = "已打开" + displayName;
        }
        catch (Exception ex)
        {
            StatusMessage = "打开" + displayName + "失败：" + ex.Message;
            _logger.Error(ex, "打开" + displayName + "失败");
        }
    }

}
