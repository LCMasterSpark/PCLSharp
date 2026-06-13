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
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
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

            MainWindow = new MainWindow(viewModel, _services.Prompts);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            LogFatalException(ex, "应用启动失败");
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            _services.Settings.SaveAsync().GetAwaiter().GetResult();
            _services.Logger.Info($"应用退出，退出码：{e.ApplicationExitCode}");
        }

        DispatcherUnhandledException -= HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
        TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
        base.OnExit(e);
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Logger.Error(e.Exception, "未处理的界面异常");
        _services?.Prompts.Confirm("PCL Sharp 遇到错误", e.Exception.Message);
        e.Handled = true;
    }

    private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException("未知未处理异常：" + e.ExceptionObject);
        LogFatalException(exception, e.IsTerminating ? "未处理的后台异常，进程即将终止" : "未处理的后台异常");
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogFatalException(e.Exception, "未观察到的后台任务异常");
        e.SetObserved();
    }

    private void LogFatalException(Exception exception, string message)
    {
        try
        {
            _services?.Logger.Error(exception, message);
        }
        catch
        {
            // Ignore logging failures while the process is already failing.
        }
    }
}
