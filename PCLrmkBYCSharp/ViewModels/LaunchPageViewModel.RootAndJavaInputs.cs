using System.IO;
using PCLrmkBYCSharp.Models;
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

        RefreshJavaSelectionForCurrentInstance(updateStatus: true);
    }

    private void RefreshJavaSelectionForCurrentInstance(bool updateStatus)
    {
        var savedJava = ResolveJavaPath(SelectedInstance?.Name);
        RefreshJavaEntryOptions(savedJava);
        var selectedJava = SelectCompatibleJavaForCurrentInstance(savedJava);
        _isRestoringJavaSelection = true;
        try
        {
            SelectedJava = selectedJava;
            SelectedJavaOption = FindJavaOption(selectedJava);
        }
        finally
        {
            _isRestoringJavaSelection = false;
        }

        OnPropertyChanged(nameof(SelectedJavaSummary));
        if (updateStatus)
        {
            StatusMessage = BuildJavaScanStatus(savedJava, selectedJava);
        }
    }

    private void RefreshJavaEntryOptions(string savedJava)
    {
        JavaEntryOptions.Clear();
        var requirement = SelectedInstance is null ? null : _javaSelector.GetRequirement(SelectedInstance);
        foreach (var java in JavaEntries)
        {
            var isCompatible = SelectedInstance is null
                || ShouldIgnoreJavaCompatibility(SelectedInstance.Name)
                || requirement?.Allows(java) == true;
            var isSaved = !string.IsNullOrWhiteSpace(savedJava)
                && string.Equals(java.PathJava, savedJava, StringComparison.OrdinalIgnoreCase);
            JavaEntryOptions.Add(new JavaEntryOption(java, isCompatible, isSaved, requirement?.DisplayText ?? "任意 Java"));
        }
    }

    private JavaEntryOption? FindJavaOption(JavaEntry? java)
    {
        return java is null
            ? null
            : JavaEntryOptions.FirstOrDefault(option => string.Equals(option.Entry.PathJava, java.PathJava, StringComparison.OrdinalIgnoreCase));
    }

    private JavaEntry? SelectCompatibleJavaForCurrentInstance(string savedJava)
    {
        if (SelectedInstance is null)
        {
            return JavaEntries.FirstOrDefault(java => string.Equals(java.PathJava, savedJava, StringComparison.OrdinalIgnoreCase))
                ?? JavaEntries.FirstOrDefault();
        }

        var savedEntry = JavaEntries.FirstOrDefault(java => string.Equals(java.PathJava, savedJava, StringComparison.OrdinalIgnoreCase));
        if (ShouldIgnoreJavaCompatibility(SelectedInstance.Name) && savedEntry is not null)
        {
            return savedEntry;
        }

        return _javaSelector.SelectBest(SelectedInstance, JavaEntries, savedJava);
    }

    private string BuildJavaScanStatus(string savedJava, JavaEntry? selectedJava)
    {
        if (SelectedInstance is null)
        {
            return $"Java 扫描完成：{JavaEntries.Count} 个";
        }

        var requirement = _javaSelector.GetRequirement(SelectedInstance);
        if (selectedJava is null)
        {
            return $"Java 扫描完成：未找到满足 {requirement.DisplayText} 的 Java";
        }

        if (!string.IsNullOrWhiteSpace(savedJava)
            && !string.Equals(selectedJava.PathJava, savedJava, StringComparison.OrdinalIgnoreCase)
            && !ShouldIgnoreJavaCompatibility(SelectedInstance.Name))
        {
            return $"已为 {SelectedInstance.Name} 自动切换到兼容 Java：{selectedJava.DisplayName}";
        }

        return $"Java 扫描完成：{JavaEntries.Count} 个，当前使用 {selectedJava.DisplayName}";
    }

    private bool ShouldIgnoreJavaCompatibility(string? instanceName)
    {
        return !string.IsNullOrWhiteSpace(instanceName)
            && _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionAdvanceJava), false);
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

        RefreshJavaEntryOptions(imported.PathJava);

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
