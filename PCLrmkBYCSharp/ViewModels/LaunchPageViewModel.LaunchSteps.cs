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
    private void HandleLaunchStepsChanged(object? sender, IReadOnlyList<LaunchStepState> steps)
    {
        var snapshot = steps.ToArray();
        InvokeOnUiSafely(() => TryRefreshLaunchSteps(snapshot), "刷新启动步骤失败");
    }

    private void TryRefreshLaunchSteps(IReadOnlyList<LaunchStepState> steps)
    {
        try
        {
            RefreshLaunchSteps(steps);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "刷新启动步骤失败");
        }
    }

    private void RefreshLaunchSteps()
    {
        RefreshLaunchSteps(_launchPipeline.Steps);
    }

    private void RefreshLaunchSteps(IReadOnlyList<LaunchStepState> steps)
    {
        if (!IsOnUiThread())
        {
            var snapshot = steps.ToArray();
            InvokeOnUiSafely(() => RefreshLaunchSteps(snapshot), "刷新启动步骤失败");
            return;
        }

        lock (_stepsSync)
        {
            Steps.Clear();
            foreach (var step in steps)
            {
                Steps.Add(step);
            }
        }

        UpdateFileCompletionFromSteps(steps);
        OnPropertyChanged(nameof(LaunchCurrentStepText));
        OnPropertyChanged(nameof(LaunchProgressPercent));
        OnPropertyChanged(nameof(LaunchProgressText));
    }

    private void UpdateFileCompletionFromSteps(IReadOnlyList<LaunchStepState> steps)
    {
        var step = steps.LastOrDefault(item => item.Name.Contains("补全", StringComparison.OrdinalIgnoreCase));
        if (step is null)
        {
            return;
        }

        FileCompletionSummary = step.Status switch
        {
            LaunchStepStatus.Running => "正在补全启动文件：" + step.Message,
            LaunchStepStatus.Succeeded => "启动文件补全完成：" + step.Message,
            LaunchStepStatus.Failed => "启动文件补全失败：" + step.Message,
            LaunchStepStatus.Skipped => "启动文件补全跳过：" + step.Message,
            _ => "启动文件补全：" + step.Message
        };
        lock (_fileCompletionDetailsSync)
        {
            FileCompletionDetails.Clear();
            FileCompletionDetails.Add("步骤：" + step.Name);
            FileCompletionDetails.Add("状态：" + step.Status);
            if (!string.IsNullOrWhiteSpace(step.Message))
            {
                FileCompletionDetails.Add("说明：" + step.Message);
            }
        }
    }

    private void UpdateFileCompletionFromMissingFiles(IReadOnlyList<string> missingFiles)
    {
        if (!IsOnUiThread())
        {
            var snapshot = missingFiles.ToArray();
            InvokeOnUi(() => UpdateFileCompletionFromMissingFiles(snapshot));
            return;
        }

        lock (_fileCompletionDetailsSync)
        {
            FileCompletionDetails.Clear();
            if (missingFiles.Count == 0)
            {
                FileCompletionSummary = "本地文件完整";
                FileCompletionDetails.Add("未发现缺失文件。");
                return;
            }

            FileCompletionSummary = $"仍缺少 {missingFiles.Count} 个启动文件";
            FileCompletionDetails.Add("缺失文件：" + missingFiles.Count + " 个");
            foreach (var file in missingFiles.Take(5))
            {
                FileCompletionDetails.Add("- " + file);
            }

            if (missingFiles.Count > 5)
            {
                FileCompletionDetails.Add("- 另有 " + (missingFiles.Count - 5) + " 个文件未显示");
            }
        }
    }

    private bool IsOnUiThread()
    {
        if (_isApplyingUiUpdate)
        {
            return true;
        }

        if (_dispatcher is not null)
        {
            return _dispatcher.CheckAccess();
        }

        var appDispatcher = Application.Current?.Dispatcher;
        if (appDispatcher is not null)
        {
            return appDispatcher.CheckAccess();
        }

        return _uiContext is null || SynchronizationContext.Current == _uiContext;
    }

}
