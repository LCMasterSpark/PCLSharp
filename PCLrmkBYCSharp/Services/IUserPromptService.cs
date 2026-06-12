using System.Windows;

namespace PCLrmkBYCSharp.Services;

public interface IUserPromptService
{
    bool Confirm(string title, string message);

    string? Prompt(string title, string message, string defaultValue);
}

public sealed class UserPromptService : IUserPromptService
{
    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    public string? Prompt(string title, string message, string defaultValue)
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            MinWidth = 320,
            Margin = new Thickness(0, 8, 0, 0)
        };
        input.SelectAll();

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(input);

        var window = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current?.MainWindow
        };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new System.Windows.Controls.Button { Content = "确定", MinWidth = 82, IsDefault = true };
        var cancel = new System.Windows.Controls.Button { Content = "取消", MinWidth = 82, Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        return window.ShowDialog() == true ? input.Text : null;
    }
}
