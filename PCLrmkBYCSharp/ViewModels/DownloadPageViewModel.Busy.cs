using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class DownloadPageViewModel
{
    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        if (await InvokeOnUiAsync(() => IsBusy))
        {
            return;
        }

        await InvokeOnUiAsync(() =>
        {
            IsBusy = true;
            StatusMessage = message;
        });
        try
        {
            await InvokeOnUiAsync(action);
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            await InvokeOnUiAsync(() => StatusMessage = ex.Message);
            _logger.Error(ex, "下载页操作失败");
        }
        finally
        {
            await InvokeOnUiAsync(() => IsBusy = false);
        }
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (IsOnUiThread())
        {
            action();
            return Task.CompletedTask;
        }

        if (_dispatcher is not null)
        {
            return _dispatcher.InvokeAsync(action);
        }

        var appDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            return appDispatcher.InvokeAsync(action).Task;
        }

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiContext.Post(_ =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);
            return completion.Task;
        }

        action();
        return Task.CompletedTask;
    }

    private Task<T> InvokeOnUiAsync<T>(Func<T> action)
    {
        if (IsOnUiThread())
        {
            return Task.FromResult(action());
        }

        if (_dispatcher is not null)
        {
            return _dispatcher.InvokeAsync(action);
        }

        var appDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            return appDispatcher.InvokeAsync(action).Task;
        }

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiContext.Post(_ =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);
            return completion.Task;
        }

        return Task.FromResult(action());
    }

    private async Task InvokeOnUiAsync(Func<Task> action)
    {
        if (IsOnUiThread())
        {
            await action();
            return;
        }

        if (_dispatcher is not null)
        {
            var task = await _dispatcher.InvokeAsync(action);
            await task;
            return;
        }

        var appDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (appDispatcher is not null && !appDispatcher.CheckAccess())
        {
            var task = await appDispatcher.InvokeAsync(action).Task;
            await task;
            return;
        }

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiContext.Post(async _ =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);
            await completion.Task;
            return;
        }

        await action();
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
