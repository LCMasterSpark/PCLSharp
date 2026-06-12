using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel
{
    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = message;
        try
        {
            await action();
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error(ex, "下载页操作失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifySelectedResourceDownloadStateChanged()
    {
        OnPropertyChanged(nameof(SelectedResourceFileSummary));
        OnPropertyChanged(nameof(SelectedResourceVersionSummary));
        OnPropertyChanged(nameof(SelectedResourcePlatformText));
        OnPropertyChanged(nameof(SelectedResourceGameVersionText));
        OnPropertyChanged(nameof(SelectedResourceLoaderText));
        OnPropertyChanged(nameof(SelectedResourceDependencyText));
        OnPropertyChanged(nameof(SelectedResourceDependencyListText));
        OnPropertyChanged(nameof(CanDownloadSelectedResourceFile));
        OnPropertyChanged(nameof(ResourceDownloadActionText));
        NotifyDownloadInfoChanged();
        DownloadSelectedResourceFileCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyDownloadInfoChanged()
    {
        OnPropertyChanged(nameof(DownloadInfoTitle));
        OnPropertyChanged(nameof(DownloadInfoSummary));
        OnPropertyChanged(nameof(DownloadInfoDetails));
    }
}
