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
    private async Task ToggleSelectedInstanceStarAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        var newValue = !SelectedInstance.IsStar;
        _instanceManagement.SetStar(SelectedInstance, newValue);
        var message = newValue ? $"{SelectedInstance.Name} 已加入收藏夹" : $"{SelectedInstance.Name} 已取消收藏";
        StatusMessage = message;
        _logger.Info(message);
        await RefreshAsync();
        StatusMessage = message;
    }

    private async Task RenameSelectedInstanceAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        var oldName = SelectedInstance.Name;
        var newName = _prompts.Prompt("重命名版本", "请输入新的版本名。", oldName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "已取消重命名版本";
            return;
        }

        try
        {
            _instanceManagement.RenameInstance(SelectedInstance, newName);
            var normalizedNewName = newName.Trim();
            _settings.Set(AppSettingKeys.InstanceManageSelectedName, normalizedNewName);
            var wasLaunchVersion = string.Equals(GetLaunchInstanceName(), oldName, StringComparison.OrdinalIgnoreCase);
            if (wasLaunchVersion)
            {
                _settings.Set(AppSettingKeys.SelectedInstanceName, normalizedNewName);
                _selections.WriteSelectedInstanceName(MinecraftRootPath, normalizedNewName);
            }
            var message = wasLaunchVersion
                ? $"版本 {oldName} 已重命名为 {normalizedNewName}，启动版本已同步"
                : $"版本 {oldName} 已重命名为 {normalizedNewName}";
            StatusMessage = message;
            _logger.Info(StatusMessage);
            await RefreshAsync();
            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = "重命名版本失败：" + ex.Message;
            _logger.Error(ex, "重命名版本失败");
        }
    }

    private async Task RenameInstanceFromListAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        await RenameSelectedInstanceAsync();
    }

    private async Task CloneSelectedInstanceAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        var sourceName = SelectedInstance.Name;
        var defaultName = CreateCloneDefaultName(sourceName);
        var newName = _prompts.Prompt("复制版本", "请输入复制后的版本名。", defaultName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "已取消复制版本";
            return;
        }

        try
        {
            _instanceManagement.CloneInstance(SelectedInstance, newName);
            var normalizedNewName = newName.Trim();
            _settings.Set(AppSettingKeys.InstanceManageSelectedName, normalizedNewName);
            var message = $"版本 {sourceName} 已复制为 {normalizedNewName}，正在管理 {normalizedNewName}";
            StatusMessage = message;
            _logger.Info(StatusMessage);
            await RefreshAsync();
            SelectedInstance = Instances.FirstOrDefault(instance => string.Equals(instance.Name, normalizedNewName, StringComparison.OrdinalIgnoreCase))
                ?? SelectedInstance;
            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = "复制版本失败：" + ex.Message;
            _logger.Error(ex, "复制版本失败");
        }
    }

    private async Task CloneInstanceFromListAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        await CloneSelectedInstanceAsync();
    }

    private async Task ExportSelectedInstanceScriptAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        SaveInstanceSettingsToStore();
        var targetPath = _fileDialogs.PickSaveFile(
            "导出启动脚本",
            Directory.Exists(SelectedInstance.VersionPath) ? SelectedInstance.VersionPath : MinecraftRootPath,
            $"启动 {SelectedInstance.Name}.bat",
            "Windows 批处理脚本 (*.bat)|*.bat|所有文件 (*.*)|*.*");
        if (targetPath is null)
        {
            StatusMessage = "已取消导出启动脚本";
            return;
        }

        var result = await _launchPipeline.LaunchAsync(CreateScriptExportRequest(SelectedInstance, targetPath));
        StatusMessage = result.Success
            ? "启动脚本已导出：" + targetPath
            : string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message));
    }

    private async Task ExportSelectedInstanceModpackAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请选择一个版本";
            return;
        }

        SaveInstanceSettingsToStore();
        var initialDirectory = Directory.Exists(SelectedInstance.VersionPath) ? SelectedInstance.VersionPath : MinecraftRootPath;
        var targetPath = _fileDialogs.PickSaveFile(
            "导出整合包",
            initialDirectory,
            SanitizeExportFileName(string.IsNullOrWhiteSpace(ExportPackName) ? SelectedInstance.Name : ExportPackName) + ".mrpack",
            "Modrinth 整合包 (*.mrpack)|*.mrpack|ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*");
        if (targetPath is null)
        {
            StatusMessage = "已取消导出整合包";
            return;
        }

        try
        {
            var result = await _modpackExport.ExportModrinthAsync(
                SelectedInstance,
                ResolveGameDirectory(SelectedInstance),
                targetPath,
                string.IsNullOrWhiteSpace(ExportPackName) ? SelectedInstance.Name : ExportPackName,
                string.IsNullOrWhiteSpace(ExportPackVersion) ? "1.0.0" : ExportPackVersion,
                CreateModpackExportOptions());
            StatusMessage = $"整合包已导出：{targetPath}（{result.OverrideFileCount} 个本地文件）";
            if (result.Warnings.Count > 0)
            {
                StatusMessage += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "导出整合包失败：" + ex.Message;
            _logger.Error(ex, "导出整合包失败");
        }
    }

    private ModpackExportOptions CreateModpackExportOptions()
    {
        return new ModpackExportOptions(
            ExportIncludeConfig,
            ExportIncludeMods,
            ExportIncludeResourcePacks,
            ExportIncludeShaderPacks,
            ExportIncludeSaves,
            ExportIncludeScreenshots,
            ExportIncludeOptions,
            ExportIncludeExtraData);
    }

    private void ResetExportMetadata(MinecraftInstance? instance)
    {
        ExportPackName = instance is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(instance.CustomInfo) ? instance.Name : instance.CustomInfo;
        ExportPackVersion = "1.0.0";
    }

    private static string SanitizeExportFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Modpack" : sanitized;
    }

    private string CreateCloneDefaultName(string sourceName)
    {
        var existingNames = _allInstances
            .Select(instance => instance.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = sourceName + " - 副本";
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return sourceName + " - 副本 " + DateTime.Now.ToString("yyyyMMddHHmmss");
    }

    private async Task ToggleSelectedInstanceHiddenAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        var newType = SelectedInstance.IsHidden ? MinecraftInstanceDisplayType.Auto : MinecraftInstanceDisplayType.Hidden;
        _instanceManagement.SetDisplayType(SelectedInstance, newType);
        var message = SelectedInstance.IsHidden ? $"{SelectedInstance.Name} 已取消隐藏" : $"{SelectedInstance.Name} 已隐藏";
        StatusMessage = message;
        _logger.Info(message);
        _suppressHiddenSelectionReveal = true;
        try
        {
            await RefreshAsync();
        }
        finally
        {
            _suppressHiddenSelectionReveal = false;
        }

        StatusMessage = message;
    }

    private async Task DeleteSelectedInstanceAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        var selectedName = SelectedInstance.Name;
        if (!_prompts.Confirm("版本删除确认", $"你确定要删除版本 {selectedName} 吗？"))
        {
            StatusMessage = "已取消删除版本";
            return;
        }

        try
        {
            var wasLaunchVersion = string.Equals(GetLaunchInstanceName(), selectedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_settings.Get(AppSettingKeys.SelectedInstanceName, ""), selectedName, StringComparison.OrdinalIgnoreCase);
            _instanceManagement.DeleteInstance(SelectedInstance);
            if (wasLaunchVersion)
            {
                _settings.Set(AppSettingKeys.SelectedInstanceName, "");
                _selections.WriteSelectedInstanceName(MinecraftRootPath, "");
            }

            if (string.Equals(_settings.Get(AppSettingKeys.InstanceManageSelectedName, ""), selectedName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.Set(AppSettingKeys.InstanceManageSelectedName, "");
            }

            var message = $"版本 {selectedName} 已删除";
            StatusMessage = message;
            _logger.Info(message);
            await RefreshAsync();
            if (wasLaunchVersion)
            {
                if (SelectedInstance is null)
                {
                    _settings.Set(AppSettingKeys.SelectedInstanceName, "");
                    _selections.WriteSelectedInstanceName(MinecraftRootPath, "");
                    message = $"版本 {selectedName} 已删除，当前没有可用启动版本";
                }
                else
                {
                    _settings.Set(AppSettingKeys.SelectedInstanceName, SelectedInstance.Name);
                    _selections.WriteSelectedInstanceName(MinecraftRootPath, SelectedInstance.Name);
                    RefreshInstanceRows();
                    RaiseSelectedInstanceFeedbackChanged();
                    message = $"版本 {selectedName} 已删除，已切换到 {SelectedInstance.Name}";
                }

                await _settings.SaveAsync();
            }
            else
            {
                message = SelectedInstance is null
                    ? $"版本 {selectedName} 已删除，当前没有可管理版本"
                    : $"版本 {selectedName} 已删除，正在管理 {SelectedInstance.Name}";
            }

            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除版本失败：{ex.Message}";
            _logger.Error(ex, "删除版本失败");
        }
    }

    private async Task DeleteInstanceFromListAsync(MinecraftInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SelectedInstance = instance;
        await DeleteSelectedInstanceAsync();
    }
}
