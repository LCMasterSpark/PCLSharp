using System.Windows;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class WpfLauncherWindowHost : ILauncherWindowHost
{
    public void Close()
    {
        RunOnUi(() => Application.Current?.MainWindow?.Close());
    }

    public void Hide()
    {
        RunOnUi(() =>
        {
            if (Application.Current?.MainWindow is not { } window)
            {
                return;
            }

            window.ShowInTaskbar = false;
            window.Hide();
        });
    }

    public void Minimize()
    {
        RunOnUi(() =>
        {
            if (Application.Current?.MainWindow is { } window)
            {
                window.WindowState = WindowState.Minimized;
            }
        });
    }

    public void ShowToTop()
    {
        RunOnUi(() =>
        {
            if (Application.Current?.MainWindow is not { } window)
            {
                return;
            }

            window.ShowInTaskbar = true;
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
