using CommunityToolkit.Mvvm.ComponentModel;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.ViewModels;

public abstract class PageViewModelBase : ObservableObject
{
    protected PageViewModelBase(PageRoute route, string title, string subtitle)
    {
        Route = route;
        Title = title;
        Subtitle = subtitle;
    }

    public PageRoute Route { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public virtual Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }
}
