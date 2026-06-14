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
    private async Task<bool> EnsureLaunchLoginReadyAsync()
    {
        var issue = ValidateEffectiveLoginBeforeLaunch();
        if (issue is null)
        {
            return true;
        }

        await InvokeOnUiAsync(() =>
        {
            RefreshLaunchSteps();
            StatusMessage = issue.Message;
            LaunchDiagnostics = BuildLaunchDiagnostics(LaunchResult.Failed(issue));
            HasLaunchFileCompletionAction = false;
        });
        _logger.Warn(issue.Message);
        return false;
    }

    private LaunchValidationIssue? ValidateEffectiveLoginBeforeLaunch()
    {
        var effectiveLoginType = ResolveEffectiveLoginType(SelectedInstance?.Name);
        if (effectiveLoginType != LoginType.Ms)
        {
            return null;
        }

        if (HasValidMicrosoftAccessToken)
        {
            return null;
        }

        if (!HasMicrosoftClientId)
        {
            return new LaunchValidationIssue(
                "MicrosoftClientIdMissing",
                "正版登录需要 Microsoft Client ID。请在启动页填写 Microsoft Client ID，或设置环境变量 PCL_MS_CLIENT_ID。");
        }

        return null;
    }

    private void HandleMicrosoftDeviceCodeChanged(object? sender, EventArgs e)
    {
        InvokeOnUi(RefreshMicrosoftDeviceCodeStatus);
    }

    private void RefreshMicrosoftDeviceCodeStatus()
    {
        var info = _microsoftDeviceCodes?.Current;
        IsMicrosoftDeviceCodeActive = _microsoftDeviceCodes?.IsActive == true && info is not null;
        MicrosoftDeviceCode = IsMicrosoftDeviceCodeActive ? info?.UserCode ?? "" : "";
        MicrosoftDeviceCodeVerificationUri = IsMicrosoftDeviceCodeActive ? info?.VerificationUri ?? "" : "";
        var statusMessage = _microsoftDeviceCodes?.StatusMessage ?? "";
        MicrosoftDeviceCodeMessage = IsMicrosoftDeviceCodeActive
            ? string.IsNullOrWhiteSpace(statusMessage)
                ? "已打开微软验证网页，并复制了登录代码。请在网页中输入代码完成授权。"
                : statusMessage
            : "";
        MicrosoftDeviceCodeExpiresText = IsMicrosoftDeviceCodeActive && _microsoftDeviceCodes?.ExpiresAt is { } expiresAt
            ? "代码有效期至 " + expiresAt.LocalDateTime.ToString("HH:mm:ss")
            : "";
    }

    private void OpenMicrosoftDeviceCodePage()
    {
        if (string.IsNullOrWhiteSpace(MicrosoftDeviceCodeVerificationUri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(MicrosoftDeviceCodeVerificationUri) { UseShellExecute = true });
            StatusMessage = "已打开微软登录验证网页";
        }
        catch (Exception ex)
        {
            StatusMessage = "打开微软登录验证网页失败：" + ex.Message;
            _logger.Error(ex, "打开微软登录验证网页失败");
        }
    }

    private void CopyMicrosoftDeviceCode()
    {
        if (string.IsNullOrWhiteSpace(MicrosoftDeviceCode))
        {
            return;
        }

        if (_clipboard is null)
        {
            StatusMessage = "当前环境没有可用的剪贴板服务。";
            return;
        }

        try
        {
            _clipboard.SetText(MicrosoftDeviceCode);
            StatusMessage = "已复制微软登录代码";
        }
        catch (Exception ex)
        {
            StatusMessage = "复制微软登录代码失败：" + ex.Message;
            _logger.Error(ex, "复制微软登录代码失败");
        }
    }

    private LaunchRequest CreateRequest(bool startProcess, string saveBatchPath = "")
    {
        var effectiveJavaPath = ResolveLaunchJavaPath();
        SyncInstanceServerLoginCache(SelectedInstance?.Name);
        var effectiveLoginType = ResolveEffectiveLoginType(SelectedInstance?.Name);
        var effectiveLoginUserName = ResolveEffectiveLoginUserName(effectiveLoginType);
        var effectiveLoginPassword = ResolveEffectiveLoginPassword(effectiveLoginType);
        var effectiveLoginServer = ResolveEffectiveLoginServer(effectiveLoginType, SelectedInstance?.Name);
        return new LaunchRequest(
            SelectedInstance,
            MinecraftRootPath,
            effectiveJavaPath,
            LegacyName,
            MinMemoryMb,
            MaxMemoryMb,
            LaunchWindowWidth,
            LaunchWindowHeight,
            ExtraJvmArgs,
            ExtraGameArgs,
            startProcess,
            effectiveLoginType,
            ServerIp,
            saveBatchPath,
            LauncherVisibility,
            LaunchWindowType,
            effectiveLoginUserName,
            effectiveLoginPassword,
            effectiveLoginServer,
            RememberLogin,
            SelectedInstance is null ? "" : _gameDirectories.Resolve(SelectedInstance).Path);
    }

    private string? ResolveLaunchJavaPath()
    {
        var configuredJavaPath = ResolveJavaPath(SelectedInstance?.Name);
        if (SelectedInstance is not null && !ShouldIgnoreJavaCompatibility(SelectedInstance.Name))
        {
            if (SelectedJava is null || !_javaSelector.GetRequirement(SelectedInstance).Allows(SelectedJava))
            {
                return null;
            }

            return SelectedJava.PathJava;
        }

        return string.IsNullOrWhiteSpace(configuredJavaPath) ? SelectedJava?.PathJava : configuredJavaPath;
    }

    private LoginType ResolveEffectiveLoginType(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return SelectedLoginType;
        }

        return _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerLogin), 0) switch
        {
            1 => LoginType.Ms,
            2 => LoginType.Legacy,
            3 => LoginType.Nide,
            4 => LoginType.Auth,
            _ => SelectedLoginType
        };
    }

    private string ResolveEffectiveLoginUserName(LoginType effectiveLoginType)
    {
        return effectiveLoginType == SelectedLoginType
            ? LoginUserName
            : GetLoginUserName(effectiveLoginType);
    }

    private string ResolveEffectiveLoginPassword(LoginType effectiveLoginType)
    {
        return effectiveLoginType == SelectedLoginType
            ? LoginPassword
            : GetLoginPassword(effectiveLoginType);
    }

    private string ResolveEffectiveLoginServer(LoginType effectiveLoginType, string? instanceName)
    {
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            var mode = _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerLogin), 0);
            if (mode == 3 && effectiveLoginType == LoginType.Nide)
            {
                return _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerNide), "");
            }

            if (mode == 4 && effectiveLoginType == LoginType.Auth)
            {
                return _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerAuthServer), "");
            }
        }

        return effectiveLoginType == SelectedLoginType
            ? LoginServer
            : GetLoginServer(effectiveLoginType);
    }

    private void SyncInstanceServerLoginCache(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return;
        }

        var mode = _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerLogin), 0);
        if (mode == 3)
        {
            var server = _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerNide), "");
            if (!string.Equals(server, _settings.Get(AppSettingKeys.CacheNideServer, ""), StringComparison.Ordinal))
            {
                _settings.Set(AppSettingKeys.CacheNideAccess, "");
                _logger.Info("统一通行证服务器改变，已要求重新登录");
            }

            _settings.Set(AppSettingKeys.CacheNideServer, server);
        }
        else if (mode == 4)
        {
            var server = _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerAuthServer), "");
            if (!string.Equals(server, _settings.Get(AppSettingKeys.CacheAuthServerServer, ""), StringComparison.Ordinal))
            {
                _settings.Set(AppSettingKeys.CacheAuthAccess, "");
                _logger.Info("Authlib-Injector 服务器改变，已要求重新登录");
            }

            _settings.Set(AppSettingKeys.CacheAuthServerServer, server);
            _settings.Set(AppSettingKeys.CacheAuthServerName, _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerAuthName), ""));
            _settings.Set(AppSettingKeys.CacheAuthServerRegister, _settings.Get(GetInstanceSettingKey(instanceName, AppSettingKeys.VersionServerAuthRegister), ""));
        }
    }
}
