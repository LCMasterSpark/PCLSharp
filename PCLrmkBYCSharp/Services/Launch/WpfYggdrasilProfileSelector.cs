using System.Windows;
using System.Windows.Controls;

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

    private static YggdrasilProfileOption? SelectCore(string title, IReadOnlyList<YggdrasilProfileOption> profiles, string cachedProfileName)
    {
        var list = new ListBox
        {
            MinWidth = 360,
            MinHeight = 160,
            ItemsSource = profiles,
            DisplayMemberPath = nameof(YggdrasilProfileOption.Name),
            SelectedItem = profiles.FirstOrDefault(profile => string.Equals(profile.Name, cachedProfileName, StringComparison.OrdinalIgnoreCase)) ?? profiles[0]
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "该账号包含多个角色，请选择本次启动使用的角色。",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = "确定", MinWidth = 82, IsDefault = true };
        var cancel = new Button { Content = "取消", MinWidth = 82, Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current?.MainWindow
        };
        ok.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        return window.ShowDialog() == true ? list.SelectedItem as YggdrasilProfileOption : null;
    }
}
