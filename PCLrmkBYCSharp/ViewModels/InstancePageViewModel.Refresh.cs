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
    public void ToggleHiddenInstancesView()
    {
        if (!ShowHiddenInstances)
        {
            ShowHiddenInstances = true;
            return;
        }

        _suppressHiddenSelectionReveal = true;
        try
        {
            ShowHiddenInstances = false;
        }
        finally
        {
            _suppressHiddenSelectionReveal = false;
        }
    }

    public async Task RefreshAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = "正在扫描 Minecraft 实例...";
        try
        {
            _settings.Set(AppSettingKeys.MinecraftRootPath, MinecraftRootPath);
            var instances = await _minecraftDiscovery.ScanAsync(MinecraftRootPath);
            _allInstances.Clear();
            _allInstances.AddRange(instances);
            RefreshVisibleInstances();

            RestoreSelection();
            OnPropertyChanged(nameof(InstanceCount));
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(HiddenCount));
            StatusMessage = $"扫描完成：{InstanceCount} 个实例，{ErrorCount} 个异常";
            _logger.Info($"Minecraft 实例扫描完成：{MinecraftRootPath}，实例 {InstanceCount}，异常 {ErrorCount}");
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
            _logger.Error(ex, "Minecraft 实例扫描失败");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void RaiseSelectedInstanceFeedbackChanged()
    {
        OnPropertyChanged(nameof(SelectedInstanceDetail));
        OnPropertyChanged(nameof(VersionManagementSummary));
        OnPropertyChanged(nameof(SelectedInstanceOverview));
        OnPropertyChanged(nameof(SelectedInstanceTechnicalDetail));
        OnPropertyChanged(nameof(IsSelectedInstanceLaunchVersion));
        OnPropertyChanged(nameof(SelectedLaunchActionText));
        OnPropertyChanged(nameof(InstanceLaunchSettingsTitle));
        OnPropertyChanged(nameof(InstanceLaunchOverrideSummary));
        OnPropertyChanged(nameof(SelectedStarActionText));
        OnPropertyChanged(nameof(SelectedHiddenActionText));
        OnPropertyChanged(nameof(LocalModsDirectory));
    }
}
