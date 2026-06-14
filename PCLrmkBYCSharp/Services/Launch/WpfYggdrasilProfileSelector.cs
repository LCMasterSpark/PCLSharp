using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class WpfYggdrasilProfileSelector : IYggdrasilProfileSelector
{
    public Task<YggdrasilProfileOption?> SelectAsync(
        string title,
        IReadOnlyList<YggdrasilProfileOption> profiles,
        string cachedProfileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profiles.Count == 0)
        {
            return Task.FromResult<YggdrasilProfileOption?>(null);
        }

        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            return Application.Current.Dispatcher.InvokeAsync(() => SelectCore(title, profiles, cachedProfileName)).Task;
        }

        return Task.FromResult(SelectCore(title, profiles, cachedProfileName));
    }

    private static YggdrasilProfileOption? SelectCore(
        string title,
        IReadOnlyList<YggdrasilProfileOption> profiles,
        string cachedProfileName)
    {
        var app = Application.Current;
        var background = FindBrush("AppBackgroundBrush", Color.FromRgb(0x1E, 0x1E, 0x1E));
        var panelBrush = FindBrush("AppPanelBrush", Color.FromRgb(0x2D, 0x2D, 0x30));
        var cardBrush = FindBrush("AppCardBrush", Color.FromRgb(0x2A, 0x2A, 0x2D));
        var borderBrush = FindBrush("AppBorderBrush", Color.FromRgb(0x3F, 0x3F, 0x46));
        var textBrush = FindBrush("AppTextBrush", Colors.WhiteSmoke);
        var mutedBrush = FindBrush("AppMutedTextBrush", Color.FromRgb(0xA7, 0xA7, 0xA7));
        var accentBrush = FindBrush("AppAccentBrush", Color.FromRgb(0xA3, 0x71, 0xF7));
        var accentSoftBrush = FindBrush("AppAccentSoftBrush", Color.FromRgb(0x3B, 0x2A, 0x55));
        var buttonTemplate = app?.TryFindResource("DarkButtonTemplate") as ControlTemplate;

        var list = new ListBox
        {
            MinWidth = 420,
            MinHeight = 190,
            MaxHeight = 260,
            Margin = new Thickness(0, 14, 0, 0),
            Background = cardBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            ItemsSource = profiles,
            DisplayMemberPath = nameof(YggdrasilProfileOption.Name),
            SelectedItem = profiles.FirstOrDefault(profile => string.Equals(profile.Name, cachedProfileName, StringComparison.OrdinalIgnoreCase)) ?? profiles[0],
            ItemContainerStyle = CreateListBoxItemStyle(textBrush, mutedBrush, accentSoftBrush, accentBrush)
        };

        var titleBar = new Border
        {
            Background = accentSoftBrush,
            Padding = new Thickness(16, 10, 10, 10)
        };
        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition());
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var closeButton = CreateButton("x", 34, buttonTemplate, panelBrush, textBrush, accentBrush);
        closeButton.Padding = new Thickness(0);
        closeButton.FontSize = 14;
        Grid.SetColumn(closeButton, 1);
        titleGrid.Children.Add(closeButton);
        titleBar.Child = titleGrid;

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Background = panelBrush
        };
        panel.Children.Add(new TextBlock
        {
            Text = "\u8be5\u8d26\u53f7\u5305\u542b\u591a\u4e2a\u89d2\u8272\uff0c\u8bf7\u9009\u62e9\u672c\u6b21\u542f\u52a8\u4f7f\u7528\u7684\u89d2\u8272\u3002",
            Foreground = mutedBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        });
        panel.Children.Add(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = CreateButton("\u786e\u5b9a", 96, buttonTemplate, accentSoftBrush, textBrush, accentBrush);
        ok.IsDefault = true;
        var cancel = CreateButton("\u53d6\u6d88", 96, buttonTemplate, panelBrush, textBrush, borderBrush);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var dock = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);
        dock.Children.Add(panel);
        var root = new Border
        {
            Background = panelBrush,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            Child = dock
        };

        var window = new Window
        {
            Title = title,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            Background = background,
            Foreground = textBrush,
            Owner = app?.MainWindow
        };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                window.DragMove();
            }
        };
        ok.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };
        closeButton.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is not null)
            {
                window.DialogResult = true;
                window.Close();
            }
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
                window.Close();
                e.Handled = true;
            }
        };

        return window.ShowDialog() == true ? list.SelectedItem as YggdrasilProfileOption : null;
    }

    private static Button CreateButton(
        string text,
        double minWidth,
        ControlTemplate? template,
        Brush background,
        Brush foreground,
        Brush borderBrush)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = minWidth,
            Padding = new Thickness(14, 7, 14, 7),
            Background = background,
            Foreground = foreground,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            FocusVisualStyle = null
        };
        if (template is not null)
        {
            button.Template = template;
        }

        return button;
    }

    private static Style CreateListBoxItemStyle(Brush textBrush, Brush mutedBrush, Brush selectedBrush, Brush accentBrush)
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(4, 3, 4, 3)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "ItemBorder";
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);

        var template = new ControlTemplate(typeof(ListBoxItem))
        {
            VisualTree = borderFactory
        };
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, selectedBrush, "ItemBorder"));
        selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, accentBrush, "ItemBorder"));
        var hoverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, mutedBrush, "ItemBorder"));
        template.Triggers.Add(selectedTrigger);
        template.Triggers.Add(hoverTrigger);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Brush FindBrush(string key, Color fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }
}
