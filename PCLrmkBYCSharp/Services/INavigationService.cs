using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Services;

public interface INavigationService
{
    IReadOnlyList<PageNavigationItem> Pages { get; }

    PageViewModelBase CurrentPage { get; }

    void Navigate(PageRoute route);
}
