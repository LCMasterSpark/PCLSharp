using System.Windows;
using System.Windows.Controls;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Views.Pages;

public partial class LaunchPage : UserControl
{
    public LaunchPage()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
    }

    private async void HandleLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LaunchPageViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
