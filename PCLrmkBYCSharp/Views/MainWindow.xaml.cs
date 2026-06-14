using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly UserPromptService? _prompts;
    private UserPromptRequest? _activePrompt;
    private IInputElement? _promptPreviousFocus;

    public MainWindow(MainWindowViewModel viewModel, UserPromptService? prompts = null)
    {
        _viewModel = viewModel;
        _prompts = prompts;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += HandleLoaded;
        Closing += HandleClosing;
        Closed += HandleClosed;
        if (_prompts is not null)
        {
            _prompts.PromptRequested += HandlePromptRequested;
        }
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
        if (_viewModel is IWindowCloseGuard closeGuard && !closeGuard.CanClose())
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

    private void HandleClosed(object? sender, EventArgs e)
    {
        if (_prompts is not null)
        {
            _prompts.PromptRequested -= HandlePromptRequested;
        }

        _activePrompt?.Complete(false);
        _activePrompt = null;
    }

    private void HandlePromptRequested(object? sender, UserPromptRequest request)
    {
        _promptPreviousFocus = Keyboard.FocusedElement;
        _activePrompt = request;
        request.Completed += HandlePromptCompleted;
        PromptOverlay.DataContext = request;
        PromptOverlay.Visibility = Visibility.Visible;
        PromptOverlay.Focus();
        PromptInputBox.Visibility = request.InputVisibility;
        PromptChoiceList.Visibility = request.ChoiceVisibility;
        if (request.AcceptsInput)
        {
            PromptInputBox.Focus();
            PromptInputBox.SelectAll();
        }
        else if (request.AcceptsChoice)
        {
            PromptChoiceList.Focus();
        }
        else
        {
            PromptOkButton.Focus();
        }
    }

    private void HandlePromptCompleted(object? sender, EventArgs e)
    {
        if (sender is UserPromptRequest request)
        {
            request.Completed -= HandlePromptCompleted;
        }

        PromptOverlay.Visibility = Visibility.Collapsed;
        PromptOverlay.DataContext = null;
        _activePrompt = null;
        RestorePromptFocus();
    }

    private void PromptOkButton_Click(object sender, RoutedEventArgs e)
    {
        _activePrompt?.Complete(true);
    }

    private void PromptCancelButton_Click(object sender, RoutedEventArgs e)
    {
        _activePrompt?.Complete(false);
    }

    private void PromptOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_activePrompt is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _activePrompt.Complete(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            _activePrompt.Complete(true);
            e.Handled = true;
        }
    }

    private void RestorePromptFocus()
    {
        var previousFocus = _promptPreviousFocus;
        _promptPreviousFocus = null;
        if (previousFocus is UIElement uiElement && uiElement.IsVisible && uiElement.IsEnabled)
        {
            uiElement.Focus();
        }
        else if (previousFocus is ContentElement contentElement)
        {
            contentElement.Focus();
        }
    }
}
