using System.Windows;
using System.Windows.Threading;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.ViewModels;
using PCLrmkBYCSharp.Views;

namespace PCLrmkBYCSharp;

public partial class App : Application
{
    private AppServices? _services;

    public App()
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = AppServices.Create(Dispatcher);
        _services.Paths.EnsureCreated();
        _services.Logger.Initialize();
        _services.Logger.Info("应用启动");
        await _services.Settings.LoadAsync();

        var viewModel = new MainWindowViewModel(
            _services.Navigation,
            _services.Settings,
            _services.Logger,
            _services.ExitGuard);

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            _services.Settings.SaveAsync().GetAwaiter().GetResult();
            _services.Logger.Info($"应用退出，退出码：{e.ApplicationExitCode}");
        }

        DispatcherUnhandledException -= HandleDispatcherUnhandledException;
        base.OnExit(e);
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Logger.Error(e.Exception, "未处理的界面异常");
        MessageBox.Show(e.Exception.Message, "PCL Sharp", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
