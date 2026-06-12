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
    public async Task RefreshLocalModsAsync()
    {
        if (IsScanningLocalMods)
        {
            return;
        }

        if (SelectedInstance is null)
        {
            _allLocalMods.Clear();
            _localModUpdateInfos.Clear();
            _selectedLocalModKeys.Clear();
            LocalMods.Clear();
            RaiseLocalModPropertiesChanged();
            return;
        }

        IsScanningLocalMods = true;
        try
        {
            var modsDirectory = LocalModsDirectory;
            _allLocalMods.Clear();
            _allLocalMods.AddRange(await _localMods.ScanAsync(modsDirectory));
            _localModUpdateInfos.Clear();
            PruneSelectedLocalModKeys(_allLocalMods);
            RefreshLocalModRows();
            StatusMessage = $"已刷新 {SelectedInstance.Name} 的 Mod 列表：{_allLocalMods.Count} 个";
            _logger.Info($"刷新本地 Mod 列表：{SelectedInstance.Name}，{modsDirectory}，{_allLocalMods.Count} 个");
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新 Mod 列表失败：" + ex.Message;
            _logger.Error(ex, "刷新 Mod 列表失败");
        }
        finally
        {
            IsScanningLocalMods = false;
        }
    }

    public async Task CompleteSelectedInstanceFilesAsync()
    {
        if (IsCompletingFiles)
        {
            return;
        }

        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个实例";
            return;
        }

        if (SelectedInstance.HasError)
        {
            StatusMessage = "当前实例状态异常，无法补全文件：" + SelectedInstance.ErrorMessage;
            return;
        }

        if (DisableFileCheck)
        {
            StatusMessage = "当前版本已关闭文件校验，已跳过文件补全";
            FileCompletionSummary = StatusMessage;
            FileCompletionDetails.Clear();
            FileCompletionDetails.Add("如需补全文件，请先在版本设置中取消“关闭文件校验”。");
            return;
        }

        var completionInstanceName = SelectedInstance.Name;
        IsCompletingFiles = true;
        try
        {
            var request = CreateCompletionRequest(SelectedInstance);
            IReadOnlyList<string> currentMissing = [];
            var downloaded = 0;
            var planned = 0;
            FileCompletionTaskName = SelectedInstance.Name + " 文件补全";
            FileCompletionSummary = "正在检查缺失文件";
            FileCompletionDetails.Clear();
            for (var pass = 1; pass <= 3; pass++)
            {
                var completion = await _fileCompleter.BuildCompletionPlanAsync(request, currentMissing);
                var missingSet = completion.MissingFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (missingSet.Count == 0)
                {
                    var message = downloaded == 0
                        ? $"{SelectedInstance.Name} 文件完整，无需补全"
                        : $"{SelectedInstance.Name} 文件补全完成：{downloaded} 个文件";
                    StatusMessage = message;
                    FileCompletionSummary = downloaded == 0
                        ? "文件完整，无需补全"
                        : $"补全完成：计划 {planned} 个，成功 {downloaded} 个";
                    FileCompletionDetails.Clear();
                    FileCompletionDetails.Add(downloaded == 0 ? "未发现缺失文件。" : "可在下载页查看本次补全任务记录。");
                    if (downloaded > 0 && !string.IsNullOrWhiteSpace(FileCompletionTaskName))
                    {
                        FileCompletionDetails.Add("下载任务：" + FileCompletionTaskName);
                    }
                    await RecordFileCompletionFeedbackAsync(completionInstanceName, message, succeeded: true);
                    _logger.Info(message);
                    await RefreshAsync();
                    StatusMessage = message;
                    return;
                }

                var downloads = completion.Downloads
                    .Where(file => missingSet.Contains(file.LocalPath))
                    .DistinctBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (downloads.Count == 0)
                {
                    var unresolvableCount = completion.UnresolvableMissingFiles.Count;
                    StatusMessage = unresolvableCount > 0
                        ? $"缺少 {missingSet.Count} 个文件，其中 {unresolvableCount} 个无法自动补全"
                        : $"缺少 {missingSet.Count} 个文件，但当前版本 JSON 未提供可用下载信息";
                    FileCompletionSummary = unresolvableCount > 0
                        ? $"仍缺少 {missingSet.Count} 个文件，{unresolvableCount} 个无法自动补全"
                        : $"仍缺少 {missingSet.Count} 个文件，未找到可用下载信息";
                    FillFileCompletionDetails(completion.MissingFiles, completion.UnresolvableMissingFiles);
                    await RecordFileCompletionFeedbackAsync(completionInstanceName, StatusMessage, succeeded: false);
                    _logger.Warn(StatusMessage);
                    return;
                }

                StatusMessage = $"正在补全 {SelectedInstance.Name}：第 {pass} 轮，{downloads.Count} 个文件";
                var snapshot = await _downloadManager.DownloadAsync(SelectedInstance.Name + " 文件补全", downloads);
                planned += downloads.Count;
                FileCompletionTaskName = snapshot.Name;
                FileCompletionSummary = $"第 {pass} 轮补全：计划下载 {downloads.Count} 个文件，仍缺 {missingSet.Count} 个";
                FileCompletionDetails.Clear();
                FileCompletionDetails.Add("下载任务：" + FileCompletionTaskName);
                FileCompletionDetails.Add("本轮文件：" + downloads.Count + " 个");
                AddFileCompletionSamples(completion.MissingFiles);
                if (completion.UnresolvableMissingFiles.Count > 0)
                {
                    FileCompletionDetails.Add("无法自动补全：" + completion.UnresolvableMissingFiles.Count + " 个");
                }
                if (snapshot.State != DownloadTaskState.Succeeded)
                {
                    StatusMessage = "文件补全失败：" + snapshot.Message;
                    await RecordFileCompletionFeedbackAsync(completionInstanceName, StatusMessage, succeeded: false);
                    return;
                }

                downloaded += downloads.Count;
                currentMissing = completion.MissingFiles;
            }

            var finalCompletion = await _fileCompleter.BuildCompletionPlanAsync(request, currentMissing);
            StatusMessage = finalCompletion.MissingFiles.Count == 0
                ? $"{SelectedInstance.Name} 文件补全完成：{downloaded} 个文件"
                : $"补全后仍缺少 {finalCompletion.MissingFiles.Count} 个文件";
            FileCompletionSummary = finalCompletion.MissingFiles.Count == 0
                ? $"补全完成：计划 {planned} 个，成功 {downloaded} 个"
                : $"补全后仍缺少 {finalCompletion.MissingFiles.Count} 个文件";
            FillFileCompletionDetails(finalCompletion.MissingFiles, finalCompletion.UnresolvableMissingFiles);
            await RecordFileCompletionFeedbackAsync(completionInstanceName, StatusMessage, finalCompletion.MissingFiles.Count == 0);
        }
        catch (Exception ex)
        {
            StatusMessage = "文件补全失败：" + ex.Message;
            FileCompletionSummary = "补全失败：" + ex.Message;
            await RecordFileCompletionFeedbackAsync(completionInstanceName, StatusMessage, succeeded: false);
            _logger.Error(ex, "实例文件补全失败");
        }
        finally
        {
            IsCompletingFiles = false;
        }
    }

    private async Task RecordFileCompletionFeedbackAsync(string instanceName, string message, bool succeeded)
    {
        _settings.Set(AppSettingKeys.LastFileCompletionInstanceName, instanceName);
        _settings.Set(AppSettingKeys.LastFileCompletionMessage, message);
        _settings.Set(AppSettingKeys.LastFileCompletionSucceeded, succeeded);
        await _settings.SaveAsync();
    }

    private void FillFileCompletionDetails(IReadOnlyList<string> missingFiles, IReadOnlyList<string>? unresolvableFiles = null)
    {
        FileCompletionDetails.Clear();
        if (missingFiles.Count == 0)
        {
            FileCompletionDetails.Add("缺失文件已清空。");
            if (!string.IsNullOrWhiteSpace(FileCompletionTaskName))
            {
                FileCompletionDetails.Add("下载任务：" + FileCompletionTaskName);
            }

            return;
        }

        FileCompletionDetails.Add("仍缺文件：" + missingFiles.Count + " 个");
        if (unresolvableFiles?.Count > 0)
        {
            FileCompletionDetails.Add("无法自动补全：" + unresolvableFiles.Count + " 个");
            AddFileCompletionSamples(unresolvableFiles);
            return;
        }

        AddFileCompletionSamples(missingFiles);
    }

    private void AddFileCompletionSamples(IReadOnlyList<string> files)
    {
        foreach (var file in files.Take(5))
        {
            FileCompletionDetails.Add("- " + file);
        }

        if (files.Count > 5)
        {
            FileCompletionDetails.Add("- 另有 " + (files.Count - 5) + " 个文件未显示");
        }
    }

}
