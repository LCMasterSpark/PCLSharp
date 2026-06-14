using System.Windows;
using System.Windows.Media;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class UiThemeServiceTests
{
    [Fact]
    public void ApplyUpdatesExistingBrushesAndAccessibilityResources()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        settings.Set(AppSettingKeys.AccessibilityHighContrast, true);
        settings.Set(AppSettingKeys.AccessibilityLargeText, true);
        settings.Set(AppSettingKeys.AccessibilityReducedMotion, true);
        settings.Set(AppSettingKeys.UiAnimation, true);
        settings.Set(AppSettingKeys.UiScalePercent, 90);
        var resources = CreateThemeResources();
        var originalAccent = Assert.IsType<SolidColorBrush>(resources["AppAccentBrush"]);

        new UiThemeService(resources).Apply(settings);

        Assert.Same(originalAccent, resources["AppAccentBrush"]);
        Assert.Equal(Color.FromRgb(0xCB, 0xA6, 0xFF), originalAccent.Color);
        Assert.Equal(Color.FromRgb(0x8A, 0x7D, 0xD6), Assert.IsType<SolidColorBrush>(resources["AppBorderBrush"]).Color);
        Assert.Equal(1.12d, Assert.IsType<double>(resources["AppUiScale"]));
        Assert.Equal(14.6d, Assert.IsType<double>(resources["AppBaseFontSize"]));
        Assert.False(Assert.IsType<bool>(resources["AppAnimationsEnabled"]));
    }

    [Fact]
    public void ApplyReplacesFrozenBrushes()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var resources = CreateThemeResources();
        var originalAccent = Assert.IsType<SolidColorBrush>(resources["AppAccentBrush"]);
        originalAccent.Freeze();

        new UiThemeService(resources).Apply(settings);

        var replacementAccent = Assert.IsType<SolidColorBrush>(resources["AppAccentBrush"]);
        Assert.NotSame(originalAccent, replacementAccent);
        Assert.Equal(Color.FromRgb(0xA3, 0x71, 0xF7), replacementAccent.Color);
    }

    private static ResourceDictionary CreateThemeResources()
    {
        return new ResourceDictionary
        {
            ["AppBackgroundBrush"] = new SolidColorBrush(Colors.Black),
            ["AppSurfaceBrush"] = new SolidColorBrush(Colors.Black),
            ["AppPanelBrush"] = new SolidColorBrush(Colors.Black),
            ["AppCardBrush"] = new SolidColorBrush(Colors.Black),
            ["AppStatusBrush"] = new SolidColorBrush(Colors.Black),
            ["AppBorderBrush"] = new SolidColorBrush(Colors.Black),
            ["AppTextBrush"] = new SolidColorBrush(Colors.Black),
            ["AppMutedTextBrush"] = new SolidColorBrush(Colors.Black),
            ["AppAccentBrush"] = new SolidColorBrush(Colors.Black),
            ["AppAccentSoftBrush"] = new SolidColorBrush(Colors.Black),
            ["AppHoverBrush"] = new SolidColorBrush(Colors.Black)
        };
    }
}
