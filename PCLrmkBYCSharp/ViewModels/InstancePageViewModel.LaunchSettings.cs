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
    private async Task SaveInstanceLaunchSettingsAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个实例";
            return;
        }

        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchMinMemoryMb, MinMemoryMb);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchMaxMemoryMb, MaxMemoryMb);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionRamType, VersionRamType);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionRamCustom, VersionRamCustom);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchWindowWidth, LaunchWindowWidth);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchWindowHeight, LaunchWindowHeight);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentTitle, VersionArgumentTitle);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentInfo, VersionCustomInfo);
        _instanceManagement.SetCustomInfo(SelectedInstance, VersionCustomInfo);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceJvm, ExtraJvmArgs);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceGame, ExtraGameArgs);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceRun, VersionAdvanceRun);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceRunWait, VersionAdvanceRunWait);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentJavaSelect, VersionJavaPath);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerEnter, ServerIp);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerLogin, VersionServerLogin);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerNide, VersionServerNide);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerAuthServer, VersionServerAuthServer);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerAuthRegister, VersionServerAuthRegister);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerAuthName, VersionServerAuthName);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceGC, VersionGc);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionRamOptimize, VersionRamOptimize);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceDisableJLW, DisableJlw);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceDisableLUA, DisableLua);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceDisableModUpdate, DisableModUpdate);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceJava, IgnoreJavaCompatibility);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceAssetsV2, DisableFileCheck);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentIndieV2, VersionIsolationEnabled);
        _instanceManagement.SetDisplayType(SelectedInstance, VersionDisplayType);
        var selectedName = SelectedInstance.Name;
        await _settings.SaveAsync();
        var message = $"{selectedName} 的启动设置已保存";
        StatusMessage = message;
        _logger.Info($"保存实例启动设置：{selectedName}");
        await RefreshAsync();
        SelectedInstance = Instances.FirstOrDefault(instance => string.Equals(instance.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? SelectedInstance;
        LoadLaunchSettings(SelectedInstance?.Name);
        RaiseSelectedInstanceFeedbackChanged();
        StatusMessage = message;
    }

    private async Task ResetInstanceLaunchSettingsAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请选择一个版本。";
            return;
        }

        var selectedName = SelectedInstance.Name;
        if (!_prompts.Confirm("恢复全局设置", $"确定要清除 {selectedName} 的实例单独启动设置吗？"))
        {
            StatusMessage = "已取消恢复全局设置";
            return;
        }

        foreach (var key in InstanceLaunchSettingKeys)
        {
            _settings.Reset(GetInstanceSettingKey(selectedName, key));
        }

        _instanceManagement.SetCustomInfo(SelectedInstance, "");
        _instanceManagement.SetDisplayType(SelectedInstance, MinecraftInstanceDisplayType.Auto);
        await _settings.SaveAsync();
        StatusMessage = $"{selectedName} 已恢复为跟随全局启动设置";
        _logger.Info(StatusMessage);
        await RefreshAsync();
        SelectedInstance = Instances.FirstOrDefault(instance => string.Equals(instance.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? SelectedInstance;
        LoadLaunchSettings(SelectedInstance?.Name);
        RaiseSelectedInstanceFeedbackChanged();
        StatusMessage = $"{selectedName} 已恢复为跟随全局启动设置";
    }

    private LaunchRequest CreateCompletionRequest(MinecraftInstance instance)
    {
        var loginType = ResolveVersionServerLoginType();
        return new LaunchRequest(
            instance,
            MinecraftRootPath,
            VersionJavaPath,
            _settings.Get(AppSettingKeys.LoginLegacyName, "Steve"),
            MinMemoryMb,
            MaxMemoryMb,
            LaunchWindowWidth,
            LaunchWindowHeight,
            ExtraJvmArgs,
            ExtraGameArgs,
            false,
            loginType,
            ServerIp,
            LoginServer: ResolveVersionLoginServer(loginType),
            GameDirectory: ResolveGameDirectory(instance));
    }

    private LoginType ResolveVersionServerLoginType()
    {
        return VersionServerLogin switch
        {
            1 => LoginType.Ms,
            2 => LoginType.Legacy,
            3 => LoginType.Nide,
            4 => LoginType.Auth,
            _ => LoginType.Legacy
        };
    }

    private string ResolveVersionLoginServer(LoginType loginType)
    {
        return loginType switch
        {
            LoginType.Nide => VersionServerNide,
            LoginType.Auth => VersionServerAuthServer,
            _ => ""
        };
    }

    private void SaveInstanceSettingsToStore()
    {
        if (SelectedInstance is null)
        {
            return;
        }

        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchMinMemoryMb, MinMemoryMb);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchMaxMemoryMb, MaxMemoryMb);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionRamType, VersionRamType);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionRamCustom, VersionRamCustom);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchWindowWidth, LaunchWindowWidth);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.LaunchWindowHeight, LaunchWindowHeight);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentTitle, VersionArgumentTitle);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentInfo, VersionCustomInfo);
        _instanceManagement.SetCustomInfo(SelectedInstance, VersionCustomInfo);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceJvm, ExtraJvmArgs);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceGame, ExtraGameArgs);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceRun, VersionAdvanceRun);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceRunWait, VersionAdvanceRunWait);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentJavaSelect, VersionJavaPath);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerEnter, ServerIp);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerLogin, VersionServerLogin);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerNide, VersionServerNide);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerAuthServer, VersionServerAuthServer);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerAuthRegister, VersionServerAuthRegister);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionServerAuthName, VersionServerAuthName);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceGC, VersionGc);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionRamOptimize, VersionRamOptimize);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceDisableJLW, DisableJlw);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceDisableLUA, DisableLua);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceDisableModUpdate, DisableModUpdate);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceJava, IgnoreJavaCompatibility);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionAdvanceAssetsV2, DisableFileCheck);
        SetInstanceSetting(SelectedInstance.Name, AppSettingKeys.VersionArgumentIndieV2, VersionIsolationEnabled);
        _instanceManagement.SetDisplayType(SelectedInstance, VersionDisplayType);
    }

    private LaunchRequest CreateScriptExportRequest(MinecraftInstance instance, string targetPath)
    {
        return new LaunchRequest(
            instance,
            MinecraftRootPath,
            NormalizeVersionJavaPath(VersionJavaPath),
            _settings.Get(AppSettingKeys.LoginLegacyName, "Steve").Split('¨', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Steve",
            MinMemoryMb,
            MaxMemoryMb,
            LaunchWindowWidth,
            LaunchWindowHeight,
            ExtraJvmArgs,
            ExtraGameArgs,
            false,
            LoginType.Legacy,
            ServerIp,
            targetPath,
            GameDirectory: ResolveGameDirectory(instance));
    }

    private static string? NormalizeVersionJavaPath(string javaPath)
    {
        return string.IsNullOrWhiteSpace(javaPath) || string.Equals(javaPath, "使用全局设置", StringComparison.OrdinalIgnoreCase)
            ? null
            : JavaEntry.ResolveSettingJavaPath(javaPath);
    }

    private static string NormalizeServerIp(string value)
    {
        return value.Replace('：', ':').Replace('。', '.');
    }
}
