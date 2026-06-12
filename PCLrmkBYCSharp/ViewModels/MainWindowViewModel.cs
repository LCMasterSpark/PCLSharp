using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Loading;

namespace PCLrmkBYCSharp.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IAppSettingsService _settings;
    private readonly IAppLoggerService _logger;
    private readonly IAppExitGuardService _exitGuard;
    private bool _isApplyingNavigation;

    [ObservableProperty]
    private PageRoute selectedRoute;

    [ObservableProperty]
    private PageViewModelBase currentPage;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private string loaderStatusText;

    public MainWindowViewModel(
        INavigationService navigation,
        IAppSettingsService settings,
        IAppLoggerService logger,
        IAppExitGuardService? exitGuard = null)
    {
        _navigation = navigation;
        _settings = settings;
        _logger = logger;
        _exitGuard = exitGuard ?? new AlwaysAllowExitGuardService();

        currentPage = _navigation.CurrentPage;
        selectedRoute = _settings.Get(AppSettingKeys.LastRoute, currentPage.Route);
        statusText = "重构外壳已就绪";
        loaderStatusText = "加载器模型：等待";
        NavigateCommand = new RelayCommand<PageRoute>(Navigate);
        OpenInstanceManagementCommand = new RelayCommand<MinecraftInstance>(OpenInstanceManagement);
        OpenDownloadManagementCommand = new RelayCommand(OpenDownloadManagement);
        ToggleHiddenVersionsCommand = new RelayCommand(ToggleHiddenVersions);

        Navigate(selectedRoute);
        if (_navigation is NavigationService navigationService)
        {
            _ = PreloadDownloadVersionsAsync(navigationService);
        }

        _ = RunLoaderDemoAsync();
    }

    public string WindowTitle => "Plain Craft Launcher Sharp";

    public IReadOnlyList<PageNavigationItem> Pages => _navigation.Pages;

    public IRelayCommand<PageRoute> NavigateCommand { get; }

    public IRelayCommand<MinecraftInstance> OpenInstanceManagementCommand { get; }

    public IRelayCommand OpenDownloadManagementCommand { get; }

    public IRelayCommand ToggleHiddenVersionsCommand { get; }

    public double InitialWindowWidth => _settings.Get(AppSettingKeys.WindowWidth, 1040d);

    public double InitialWindowHeight => _settings.Get(AppSettingKeys.WindowHeight, 640d);

    public double InitialWindowLeft => _settings.Get(AppSettingKeys.WindowLeft, double.NaN);

    public double InitialWindowTop => _settings.Get(AppSettingKeys.WindowTop, double.NaN);

    public bool CanClose()
    {
        return _exitGuard.CanExit();
    }

    partial void OnSelectedRouteChanged(PageRoute value)
    {
        if (!_isApplyingNavigation)
        {
            Navigate(value);
        }
    }

    public void SaveWindowPlacement(double width, double height, double left, double top)
    {
        _settings.Set(AppSettingKeys.WindowWidth, width);
        _settings.Set(AppSettingKeys.WindowHeight, height);

        if (!double.IsNaN(left) && !double.IsInfinity(left))
        {
            _settings.Set(AppSettingKeys.WindowLeft, left);
        }

        if (!double.IsNaN(top) && !double.IsInfinity(top))
        {
            _settings.Set(AppSettingKeys.WindowTop, top);
        }

        _settings.SaveAsync().GetAwaiter().GetResult();
        _logger.Info("窗口位置与尺寸已保存");
    }

    private void OpenInstanceManagement(MinecraftInstance? instance)
    {
        var target = instance;
        if (CurrentPage is LaunchPageViewModel launchPage)
        {
            target ??= launchPage.SelectedInstance;
            launchPage.DismissVersionSelector();
        }

        if (target is not null)
        {
            _settings.Set(AppSettingKeys.InstanceManageSelectedName, target.Name);
        }

        Navigate(PageRoute.Instance, prepareLaunchSelectionForManagement: false);
    }

    private void OpenDownloadManagement()
    {
        Navigate(PageRoute.Download, prepareLaunchSelectionForManagement: false);
    }

    private void ToggleHiddenVersions()
    {
        switch (CurrentPage)
        {
            case LaunchPageViewModel launchPage:
                launchPage.ShowHiddenVersions = !launchPage.ShowHiddenVersions;
                StatusText = launchPage.ShowHiddenVersions ? "正在查看隐藏版本" : "正在查看可用版本";
                _logger.Info("F11 切换启动页隐藏版本可见性：" + launchPage.ShowHiddenVersions);
                break;
            case InstancePageViewModel instancePage:
                instancePage.ToggleHiddenInstancesView();
                StatusText = instancePage.ShowHiddenInstances ? "正在查看隐藏版本" : "正在查看可用版本";
                _logger.Info("F11 切换实例页隐藏版本可见性：" + instancePage.ShowHiddenInstances);
                break;
        }
    }

    private void Navigate(PageRoute route)
    {
        Navigate(route, prepareLaunchSelectionForManagement: true);
    }

    private void Navigate(PageRoute route, bool prepareLaunchSelectionForManagement)
    {
        if (prepareLaunchSelectionForManagement && route == PageRoute.Instance && CurrentPage is LaunchPageViewModel launchPage)
        {
            launchPage.PrepareSelectedVersionForManagement();
        }

        _navigation.Navigate(route);
        _isApplyingNavigation = true;
        try
        {
            SelectedRoute = route;
        }
        finally
        {
            _isApplyingNavigation = false;
        }

        CurrentPage = _navigation.CurrentPage;
        StatusText = $"当前页面：{CurrentPage.Title}";
        _settings.Set(AppSettingKeys.LastRoute, route);
        _logger.Info($"切换页面：{CurrentPage.Title} ({route})");
        _ = ActivateCurrentPageAsync(CurrentPage);
        _ = SaveSettingsAsync();
    }

    private async Task ActivateCurrentPageAsync(PageViewModelBase page)
    {
        try
        {
            await page.OnNavigatedToAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "页面激活失败：" + page.Title);
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存设置失败");
        }
    }

    private async Task PreloadDownloadVersionsAsync(NavigationService navigationService)
    {
        try
        {
            await navigationService.PreloadDownloadVersionsAsync();
            _logger.Info("下载页版本列表已完成启动预热");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "下载页版本列表启动预热失败");
        }
    }

    private async Task RunLoaderDemoAsync()
    {
        var task = new LoadingTask<string, string>(
            "基础加载器演示",
            "完成",
            async (input, cancellationToken, progress) =>
            {
                progress.Report(0.35);
                await Task.Delay(80, cancellationToken);
                progress.Report(0.75);
                await Task.Delay(80, cancellationToken);
                return input;
            });

        try
        {
            LoaderStatusText = $"加载器模型：{task.State}";
            await task.RunAsync();
            LoaderStatusText = $"加载器模型：{task.State}";
            _logger.Info("加载器演示任务已完成");
        }
        catch (Exception ex)
        {
            LoaderStatusText = $"加载器模型：{task.State}";
            _logger.Error(ex, "加载器演示任务失败");
        }
    }
}
