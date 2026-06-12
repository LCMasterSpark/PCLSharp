using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += HandleLoaded;
        Closing += HandleClosing;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Width = Math.Max(MinWidth, _viewModel.InitialWindowWidth);
        Height = Math.Max(MinHeight, _viewModel.InitialWindowHeight);

        if (!double.IsNaN(_viewModel.InitialWindowLeft) && !double.IsNaN(_viewModel.InitialWindowTop))
        {
            Left = _viewModel.InitialWindowLeft;
            Top = _viewModel.InitialWindowTop;
        }
    }

    private void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CanClose())
        {
            e.Cancel = true;
            return;
        }

        _viewModel.SaveWindowPlacement(Width, Height, Left, Top);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
