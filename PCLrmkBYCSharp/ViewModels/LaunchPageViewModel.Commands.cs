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
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await RunBusyAsync("正在自动准备实例和 Java...", async () =>
        {
            await RefreshInstancesCoreAsync();
            var selectionRecoveryStatusMessage = _selectionRecoveryStatusMessage;
            await ScanJavaCoreAsync();
            if (!string.IsNullOrWhiteSpace(selectionRecoveryStatusMessage))
            {
                StatusMessage = selectionRecoveryStatusMessage;
            }
        });
    }

    public async Task RefreshInstancesAsync()
    {
        await RunBusyAsync("正在扫描实例...", RefreshInstancesCoreAsync);
    }

    public async Task ScanJavaAsync()
    {
        await RunBusyAsync("正在扫描 Java...", ScanJavaCoreAsync);
    }

    public async Task LaunchGameAsync()
    {
        await RunBusyAsync("正在准备启动游戏...", async cancellationToken =>
        {
            await LaunchGameCoreAsync(cancellationToken);
        }, "已取消启动");
    }

    public async Task<HelpActionResult> ExecuteCustomLaunchEventAsync(string eventData, CancellationToken cancellationToken = default)
    {
        LaunchResult? launchResult = null;
        var handlerStatus = "";
        await RunBusyAsync("正在执行自定义启动事件...", async innerCancellationToken =>
        {
            if (_allInstances.Count == 0)
            {
                await RefreshInstancesCoreAsync();
            }

            handlerStatus = ApplyCustomLaunchEventData(eventData);
            if (!string.IsNullOrWhiteSpace(handlerStatus))
            {
                return;
            }

            launchResult = await LaunchGameCoreAsync(innerCancellationToken);
        }, "已取消自定义启动事件");

        if (!string.IsNullOrWhiteSpace(handlerStatus))
        {
            return new HelpActionResult(false, handlerStatus);
        }

        if (launchResult is null)
        {
            return new HelpActionResult(false, StatusMessage);
        }

        return new HelpActionResult(
            launchResult.Success,
            launchResult.Success ? "已执行启动游戏事件" : StatusMessage);
    }

    public async Task GenerateProfileAsync()
    {
        await RunBusyAsync("正在生成启动参数...", async cancellationToken =>
        {
            await PrepareLaunchInputsAsync();
            if (!await EnsureLaunchLoginReadyAsync())
            {
                return;
            }

            var result = await _launchPipeline.GenerateProfileAsync(CreateRequest(startProcess: false), cancellationToken);
            await ApplyLaunchResultOnUiAsync(result, "启动参数已生成");
        }, "已取消生成启动参数");
    }

    private async Task<LaunchResult?> LaunchGameCoreAsync(CancellationToken cancellationToken)
    {
        SaveLaunchSettings();
        if (Instances.Count == 0)
        {
            await RefreshInstancesCoreAsync();
        }

        if (JavaEntries.Count == 0)
        {
            await ScanJavaCoreAsync();
        }

        if (!await EnsureLaunchLoginReadyAsync())
        {
            return null;
        }

        var result = await _launchPipeline.LaunchAsync(CreateRequest(startProcess: true), cancellationToken);
        await ApplyLaunchResultOnUiAsync(result, result.Process is null ? "启动未执行" : $"游戏进程已启动：{result.Process.Id}");
        return result;
    }

    private string ApplyCustomLaunchEventData(string eventData)
    {
        var (versionName, server) = ParseCustomLaunchEventData(eventData);
        if (!string.IsNullOrWhiteSpace(versionName) && !IsCurrentVersionAlias(versionName))
        {
            var instance = FindInstanceForCustomLaunch(versionName);
            if (instance is null)
            {
                StatusMessage = "未找到启动事件指定版本：" + versionName;
                return StatusMessage;
            }

            if (instance.HasError)
            {
                StatusMessage = string.IsNullOrWhiteSpace(instance.ErrorMessage)
                    ? $"版本 {instance.Name} 状态异常，无法启动"
                    : $"版本 {instance.Name} 状态异常，无法启动：{instance.ErrorMessage}";
                return StatusMessage;
            }

            SelectedInstance = instance;
            UpdateVersionSelectorRowRoles();
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            ServerIp = server;
        }

        return "";
    }

    private MinecraftInstance? FindInstanceForCustomLaunch(string versionName)
    {
        return _allInstances.FirstOrDefault(instance => string.Equals(instance.Name, versionName, StringComparison.OrdinalIgnoreCase))
            ?? _allInstances.FirstOrDefault(instance => string.Equals(instance.DisplayVersion, versionName, StringComparison.OrdinalIgnoreCase))
            ?? _allInstances.FirstOrDefault(instance => string.Equals(instance.Version.Id, versionName, StringComparison.OrdinalIgnoreCase));
    }

    private static (string VersionName, string Server) ParseCustomLaunchEventData(string eventData)
    {
        var parts = eventData.Split('|', 2);
        return (
            UnescapeCustomEventPart(parts.ElementAtOrDefault(0) ?? ""),
            UnescapeCustomEventPart(parts.ElementAtOrDefault(1) ?? ""));
    }

    private static string UnescapeCustomEventPart(string value)
    {
        return value.Replace("\\n", Environment.NewLine, StringComparison.Ordinal).Trim();
    }

    private static bool IsCurrentVersionAlias(string value)
    {
        return string.Equals(value, "\\current", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "current", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "当前", StringComparison.OrdinalIgnoreCase);
    }

    public async Task ExportLaunchScriptAsync()
    {
        await RunBusyAsync("正在导出启动脚本...", async cancellationToken =>
        {
            await PrepareLaunchInputsAsync();
            if (!await EnsureLaunchLoginReadyAsync())
            {
                return;
            }

            var initialDirectory = SelectedInstance?.VersionPath ?? "";
            if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
            {
                initialDirectory = Directory.Exists(MinecraftRootPath)
                    ? MinecraftRootPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            var targetPath = _fileDialogs.PickSaveFile(
                "导出启动脚本",
                initialDirectory,
                "LatestLaunch.bat",
                "Windows 批处理脚本 (*.bat)|*.bat|所有文件 (*.*)|*.*");
            if (targetPath is null)
            {
                StatusMessage = "已取消导出启动脚本";
                return;
            }

            var result = await _launchPipeline.LaunchAsync(CreateRequest(startProcess: false, saveBatchPath: targetPath), cancellationToken);
            await ApplyLaunchResultOnUiAsync(result, result.Success ? "启动脚本已导出：" + targetPath : "启动脚本导出失败");
        }, "已取消导出启动脚本");
    }

    public async Task LoginMicrosoftAccountAsync()
    {
        await LoginMicrosoftAccountCoreAsync(forceNewLogin: true, "正在打开微软网页登录...", "已取消正版网页登录", "正版账号登录完成：");
    }

    public async Task RefreshMicrosoftAccountAsync()
    {
        await LoginMicrosoftAccountCoreAsync(forceNewLogin: false, "正在刷新正版授权...", "已取消刷新正版授权", "正版授权刷新完成：");
    }

    private void ToggleMicrosoftClientIdEditor()
    {
        IsMicrosoftClientIdEditorVisible = !IsMicrosoftClientIdEditorVisible;
    }

    private async Task LoginMicrosoftAccountCoreAsync(bool forceNewLogin, string busyMessage, string canceledMessage, string successPrefix)
    {
        if (_loginService is null)
        {
            return;
        }

        await RunBusyAsync(busyMessage, async cancellationToken =>
        {
            SelectedLoginType = LoginType.Ms;
            SaveLaunchSettings();
            try
            {
                var session = await _loginService.LoginAsync(new LoginRequest(LoginType.Ms, LegacyName, LoginUserName, LoginPassword, LoginServer, RememberLogin, forceNewLogin), cancellationToken);
                await InvokeOnUiAsync(() =>
                {
                    OnMicrosoftAccountChanged();
                    StatusMessage = successPrefix + session.UserName;
                });
                _logger.Info(successPrefix + session.UserName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = ToUserFacingExceptionMessage(ex);
                await InvokeOnUiAsync(() =>
                {
                    StatusMessage = message;
                    LaunchDiagnostics = "正版登录失败：" + Environment.NewLine + message;
                });
                _logger.Error(ex, "正版账号登录失败");
            }
        }, canceledMessage);
    }

    public async Task LogoutMicrosoftAccountAsync()
    {
        ClearCurrentMicrosoftAccount();
        _microsoftDeviceCodes?.Clear();
        OnMicrosoftAccountChanged();
        StatusMessage = "已退出正版账号";
        await _settings.SaveAsync();
    }

    public void SwitchMicrosoftAccount()
    {
        if (SelectedMicrosoftAccount is null)
        {
            StatusMessage = "请先选择要切换的正版账号";
            return;
        }

        var selected = SelectedMicrosoftAccount with { LastUsedAt = DateTimeOffset.UtcNow };
        ApplyMicrosoftAccount(selected);
        UpsertMicrosoftAccount(selected);
        RefreshMicrosoftAccounts();
        OnMicrosoftAccountChanged();
        StatusMessage = "已切换正版账号：" + selected.Name;
        _ = SaveSettingsAsync();
    }

    public void DeleteSelectedMicrosoftAccount()
    {
        if (SelectedMicrosoftAccount is null)
        {
            StatusMessage = "请先选择要删除的正版账号";
            return;
        }

        var removed = SelectedMicrosoftAccount;
        var remaining = ReadMicrosoftAccounts()
            .Where(account => !IsSameMicrosoftAccount(account, removed))
            .ToArray();
        _settings.Set(AppSettingKeys.CacheMsV2AccountsJson, JsonSerializer.Serialize(remaining));
        RemoveLegacyMicrosoftAccount(removed);
        if (IsSameMicrosoftAccount(GetCurrentMicrosoftAccount(), removed))
        {
            ClearCurrentMicrosoftAccount();
        }

        RefreshMicrosoftAccounts();
        OnMicrosoftAccountChanged();
        StatusMessage = "已删除正版账号缓存：" + removed.Name;
        _ = SaveSettingsAsync();
    }

    public async Task LogoutServerAccountAsync()
    {
        if (!TryGetServerCachePrefix(out var prefix))
        {
            return;
        }

        _settings.Set("Cache" + prefix + "Access", "");
        _settings.Set("Cache" + prefix + "Client", "");
        _settings.Set("Cache" + prefix + "Uuid", "");
        _settings.Set("Cache" + prefix + "Name", "");
        _settings.Set("Cache" + prefix + "Username", "");
        _settings.Set("Cache" + prefix + "Pass", "");
        OnLoginAccountChanged();
        StatusMessage = SelectedLoginType == LoginType.Nide
            ? "已退出统一通行证账号"
            : "已退出 Authlib-Injector 账号";
        await _settings.SaveAsync();
    }

    public async Task LoginServerAccountAsync()
    {
        if (_loginService is null || !IsServerLogin)
        {
            return;
        }

        var displayName = SelectedLoginType == LoginType.Nide ? "统一通行证" : "Authlib-Injector";
        await RunBusyAsync("正在登录" + displayName + "账号...", async cancellationToken =>
        {
            SaveLaunchSettings();
            try
            {
                var session = await _loginService.LoginAsync(new LoginRequest(SelectedLoginType, LegacyName, LoginUserName, LoginPassword, LoginServer, RememberLogin), cancellationToken);
                await InvokeOnUiAsync(() =>
                {
                    OnLoginAccountChanged();
                    StatusMessage = displayName + "账号登录完成：" + session.UserName;
                });
                _logger.Info(displayName + "账号登录完成：" + session.UserName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await InvokeOnUiAsync(() =>
                {
                    StatusMessage = ex.Message;
                    LaunchDiagnostics = displayName + "登录失败：" + Environment.NewLine + ex.Message;
                });
                _logger.Error(ex, displayName + "账号登录失败");
            }
        }, "已取消" + displayName + "登录");
    }
}
