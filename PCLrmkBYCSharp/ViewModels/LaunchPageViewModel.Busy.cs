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
    private Task RunBusyAsync(string message, Func<Task> action)
    {
        return RunBusyAsync(message, _ => action(), "已取消操作");
    }

    private async Task RunBusyAsync(string message, Func<CancellationToken, Task> action, string canceledMessage)
    {
        if (IsBusy)
        {
            return;
        }

        _busyCancellation?.Dispose();
        _busyCancellation = new CancellationTokenSource();
        await InvokeOnUiAsync(() =>
        {
            Steps.Clear();
            LaunchDiagnostics = string.Empty;
            HasLaunchFileCompletionAction = false;
            OnPropertyChanged(nameof(LaunchCurrentStepText));
            OnPropertyChanged(nameof(LaunchProgressPercent));
            OnPropertyChanged(nameof(LaunchProgressText));
            IsBusy = true;
            StatusMessage = message;
            CancelBusyCommand.NotifyCanExecuteChanged();
        });
        try
        {
            await action(_busyCancellation.Token);
            await _settings.SaveAsync();
        }
        catch (OperationCanceledException)
        {
            await InvokeOnUiAsync(() =>
            {
                RefreshLaunchSteps();
                LaunchDiagnostics = string.Empty;
                StatusMessage = canceledMessage;
            });
            _logger.Warn(canceledMessage);
        }
        catch (Exception ex)
        {
            var userMessage = ToUserFacingExceptionMessage(ex);
            await InvokeOnUiAsync(() =>
            {
                RefreshLaunchSteps();
                StatusMessage = userMessage;
                LaunchDiagnostics = "错误：" + Environment.NewLine + "[Unhandled] " + userMessage;
            });
            _logger.Error(ex, "启动页操作失败");
        }
        finally
        {
            _microsoftDeviceCodes?.Clear();
            _busyCancellation.Dispose();
            _busyCancellation = null;
            await InvokeOnUiAsync(() =>
            {
                IsBusy = false;
                CancelBusyCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private void CancelBusyOperation()
    {
        if (!IsBusy || _busyCancellation is null)
        {
            return;
        }

        StatusMessage = "正在取消...";
        _logger.Warn("用户取消启动页操作");
        _busyCancellation.Cancel();
        CancelBusyCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存启动设置失败");
        }
    }
}
