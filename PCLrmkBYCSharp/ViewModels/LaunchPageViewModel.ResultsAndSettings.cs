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
    private Task ApplyLaunchResultOnUiAsync(LaunchResult result, string successMessage)
    {
        return InvokeOnUiAsync(() => ApplyLaunchResult(result, successMessage));
    }

    private void RunAsUiUpdate(Action action)
    {
        var wasApplying = _isApplyingUiUpdate;
        var previousThreadId = _uiUpdateThreadId;
        _isApplyingUiUpdate = true;
        _uiUpdateThreadId = Environment.CurrentManagedThreadId;
        try
        {
            action();
        }
        finally
        {
            _isApplyingUiUpdate = wasApplying;
            _uiUpdateThreadId = previousThreadId;
        }
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (_dispatcher is not null)
        {
            if (_dispatcher.CheckAccess())
            {
                RunAsUiUpdate(action);
                return Task.CompletedTask;
            }

            return _dispatcher.InvokeAsync(() => RunAsUiUpdate(action));
        }

        var appDispatcher = Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            return appDispatcher.InvokeAsync(() => RunAsUiUpdate(action)).Task;
        }

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiContext.Post(_ =>
            {
                try
                {
                    RunAsUiUpdate(action);
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);
            return completion.Task;
        }

        RunAsUiUpdate(action);
        return Task.CompletedTask;
    }

    private void InvokeOnUi(Action action)
    {
        if (_dispatcher is not null)
        {
            if (_dispatcher.CheckAccess())
            {
                RunAsUiUpdate(action);
            }
            else
            {
                _ = _dispatcher.InvokeAsync(() => RunAsUiUpdate(action));
            }

            return;
        }

        var appDispatcher = Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            _ = appDispatcher.InvokeAsync(() => RunAsUiUpdate(action));
            return;
        }

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => RunAsUiUpdate(action), null);
            return;
        }

        RunAsUiUpdate(action);
    }

    private void InvokeOnUiSafely(Action action, string errorMessage)
    {
        _ = InvokeOnUiSafelyAsync(action, errorMessage);
    }

    private async Task InvokeOnUiSafelyAsync(Action action, string errorMessage)
    {
        try
        {
            await InvokeOnUiAsync(action);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, errorMessage);
        }
    }

    private void ApplyLaunchResult(LaunchResult result, string successMessage)
    {
        RefreshLaunchSteps();

        if (result.Profile is not null)
        {
            CommandPreview = result.Profile.SanitizedCommandLine;
            UpdateFileCompletionFromMissingFiles(result.Profile.MissingFiles);
        }

        if (result.Success)
        {
            if (result.Profile is not null && result.Profile.MissingFiles.Count == 0)
            {
                FileCompletionSummary = "本地文件完整";
                lock (_fileCompletionDetailsSync)
                {
                    FileCompletionDetails.Clear();
                    FileCompletionDetails.Add("启动所需本地文件检查通过。");
                }
            }

            LaunchDiagnostics = string.Empty;
            HasLaunchFileCompletionAction = false;
            OnLoginAccountChanged();
            StatusMessage = successMessage;
            return;
        }

        StatusMessage = string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message));
        LaunchDiagnostics = BuildLaunchDiagnostics(result);
        HasLaunchFileCompletionAction = ShouldShowLaunchFileCompletionAction(result);
    }

    private async Task ConsumeFileCompletionFeedbackAsync()
    {
        var instanceName = _settings.Get(AppSettingKeys.LastFileCompletionInstanceName, "");
        if (string.IsNullOrWhiteSpace(instanceName)
            || SelectedInstance is null
            || !string.Equals(instanceName, SelectedInstance.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message = _settings.Get(AppSettingKeys.LastFileCompletionMessage, "");
        var succeeded = _settings.Get(AppSettingKeys.LastFileCompletionSucceeded, false);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = succeeded
                ? "实例页文件补全完成，可以重试启动。"
                : "实例页文件补全未完成，请继续处理缺失文件。";
        }

        await InvokeOnUiAsync(() =>
        {
            lock (_fileCompletionDetailsSync)
            {
                FileCompletionDetails.Clear();
                FileCompletionDetails.Add(message);
            }

            if (succeeded)
            {
                FileCompletionSummary = "实例页补全完成，可以重试启动";
                LaunchDiagnostics = string.Empty;
                HasLaunchFileCompletionAction = false;
                StatusMessage = message + " 可以重试启动。";
            }
            else
            {
                FileCompletionSummary = "实例页补全后仍需处理";
                HasLaunchFileCompletionAction = true;
                StatusMessage = message;
            }
        });

        _settings.Set(AppSettingKeys.LastFileCompletionInstanceName, "");
        _settings.Set(AppSettingKeys.LastFileCompletionMessage, "");
        _settings.Set(AppSettingKeys.LastFileCompletionSucceeded, false);
        await _settings.SaveAsync();
    }

    private static string BuildLaunchDiagnostics(LaunchResult result)
    {
        var lines = new List<string>();
        if (result.Issues.Count == 0)
        {
            lines.Add("错误：启动失败，但没有返回详细错误。");
        }
        else
        {
            lines.Add("错误：");
            foreach (var issue in result.Issues)
            {
                lines.Add($"[{issue.Code}] {issue.Message}");
            }
        }

        if (result.Profile?.MissingFiles.Count > 0)
        {
            lines.Add("");
            lines.Add($"缺失文件：{result.Profile.MissingFiles.Count} 个");
            foreach (var file in result.Profile.MissingFiles.Take(5))
            {
                lines.Add("- " + file);
            }

            if (result.Profile.MissingFiles.Count > 5)
            {
                lines.Add($"- 另有 {result.Profile.MissingFiles.Count - 5} 个文件未显示");
            }
        }

        var suggestions = BuildLaunchDiagnosticSuggestions(result);
        if (suggestions.Count > 0)
        {
            lines.Add("");
            lines.Add("建议处理：");
            foreach (var suggestion in suggestions)
            {
                lines.Add("- " + suggestion);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string ToUserFacingExceptionMessage(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("CollectionView", StringComparison.OrdinalIgnoreCase)
            && message.Contains("SourceCollection", StringComparison.OrdinalIgnoreCase))
        {
            return "界面刷新线程冲突，操作已中断。请重新点击一次；如果频繁出现，请先刷新版本列表后再启动。";
        }

        if (message.Contains("TwoWay", StringComparison.OrdinalIgnoreCase)
            && message.Contains("OneWayToSource", StringComparison.OrdinalIgnoreCase))
        {
            return "界面绑定方向异常，操作已中断。请重新打开该页面后再试。";
        }

        return string.IsNullOrWhiteSpace(message) ? ex.GetType().Name : message;
    }

    private static IReadOnlyList<string> BuildLaunchDiagnosticSuggestions(LaunchResult result)
    {
        var suggestions = new List<string>();
        foreach (var issue in result.Issues)
        {
            switch (issue.Code)
            {
                case "MissingLocalFiles":
                case "FileCompletionFailed":
                    suggestions.Add("先在“实例”页选择该版本并点击“补全文件”；如果仍失败，进入“下载”页重新安装或补全对应版本/整合包。");
                    break;
                case "JavaNotFound":
                    suggestions.Add("点击“扫描 Java”，或用“选择文件”手动选择符合该版本要求的 java.exe。");
                    break;
                case "LoginInvalid":
                    suggestions.Add("检查当前登录方式与账号状态；正版登录请重新登录，第三方登录请确认服务器地址、账号和密码。");
                    break;
                case "MicrosoftClientIdMissing":
                    suggestions.Add("填写 Microsoft Client ID，或设置环境变量 PCL_MS_CLIENT_ID；如果已有有效正版缓存，也可以直接继续使用缓存。");
                    break;
                case "MicrosoftAccountMissing":
                    suggestions.Add("先在启动页选择“正版登录”，点击“登录正版账号”完成微软授权后再启动。");
                    break;
                case "InvalidPath":
                    suggestions.Add("将 Minecraft 文件夹移动到不包含 ! 或 ; 的路径后重新扫描。");
                    break;
                case "MainClassMissing":
                case "InstanceInvalid":
                    suggestions.Add("版本 JSON 可能损坏或继承版本缺失，请在“实例”页重新扫描，必要时重新导入/安装该版本。");
                    break;
                case "ProcessStartFailed":
                    suggestions.Add("检查 Java 路径、杀毒软件拦截、文件权限以及启动参数；可先点击“生成参数”查看命令摘要。");
                    break;
                case "GameExitedEarly":
                    if (issue.Message.Contains("Java 版本过新", StringComparison.Ordinal)
                        || issue.Message.Contains("Unsupported class file major version", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add("当前 Java 对该版本或 Mod 可能过新，请点击“扫描 Java”或“选择文件”切换到推荐 Java，例如 1.18-1.20.4 通常使用 Java 17。");
                    }
                    else if (issue.Message.Contains("Java 版本过旧", StringComparison.Ordinal)
                        || issue.Message.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add("当前 Java 可能过旧，请切换到该 Minecraft 版本要求的 Java，例如 1.20.5+ 通常使用 Java 21。");
                    }
                    else if (issue.Message.Contains("内存", StringComparison.Ordinal)
                        || issue.Message.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add("提高最大内存、关闭内存优化或减少 Mod 后重试。");
                    }
                    else if (issue.Message.Contains("Mod", StringComparison.OrdinalIgnoreCase)
                        || issue.Message.Contains("Mixin", StringComparison.OrdinalIgnoreCase)
                        || issue.Message.Contains("ClassNotFound", StringComparison.OrdinalIgnoreCase)
                        || issue.Message.Contains("NoClassDefFound", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add("检查 Mod 与加载器版本是否匹配，并确认前置依赖已安装；可先禁用最近新增的 Mod 后重试。");
                    }
                    else if (issue.Message.Contains("主类", StringComparison.Ordinal)
                        || issue.Message.Contains("main class", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add("版本安装可能不完整，请在“实例”页补全文件，必要时重新安装加载器或重新导入整合包。");
                    }
                    else
                    {
                        suggestions.Add("查看最近日志中的错误行；如果看不出原因，先尝试生成参数、补全文件并重新扫描 Java。");
                    }

                    break;
                case "PatchPrepareFailed":
                    suggestions.Add("尝试在实例启动设置中关闭 JLW/LUA 相关补丁选项后重试。");
                    break;
                case "MemoryOptimizeFailed":
                    suggestions.Add("尝试关闭启动前内存优化后重试。");
                    break;
            }
        }

        if (result.Profile?.MissingFiles.Count > 0
            && !suggestions.Any(item => item.Contains("补全文件", StringComparison.Ordinal)))
        {
            suggestions.Add("检测到本地文件缺失，请先在“实例”页执行文件补全。");
        }

        return suggestions.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool ShouldShowLaunchFileCompletionAction(LaunchResult result)
    {
        return result.Profile?.MissingFiles.Count > 0
            || result.Issues.Any(issue => issue.Code is "MissingLocalFiles" or "FileCompletionFailed");
    }
}
