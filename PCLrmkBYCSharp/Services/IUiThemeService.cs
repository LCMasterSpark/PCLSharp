using System.Windows;
using System.Windows.Media;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services;

public interface IUiThemeService
{
    void Apply(IAppSettingsService settings);

    void Apply(ResourceDictionary resources, IAppSettingsService settings);
}

public sealed class UiThemeService(ResourceDictionary? applicationResources = null) : IUiThemeService
{
    public void Apply(IAppSettingsService settings)
    {
        var resources = applicationResources ?? Application.Current?.Resources;
        if (resources is not null)
        {
            Apply(resources, settings);
        }
    }

    public void Apply(ResourceDictionary resources, IAppSettingsService settings)
    {
        var highContrast = settings.Get(AppSettingKeys.AccessibilityHighContrast, false);
        SetBrush(resources, "AppBackgroundBrush", highContrast ? "#101010" : "#1E1E1E");
        SetBrush(resources, "AppSurfaceBrush", highContrast ? "#171717" : "#252526");
        SetBrush(resources, "AppPanelBrush", highContrast ? "#202020" : "#2D2D30");
        SetBrush(resources, "AppCardBrush", highContrast ? "#1C1C1F" : "#2A2A2D");
        SetBrush(resources, "AppStatusBrush", highContrast ? "#0E0E0E" : "#181818");
        SetBrush(resources, "AppBorderBrush", highContrast ? "#8A7DD6" : "#3F3F46");
        SetBrush(resources, "AppTextBrush", highContrast ? "#FFFFFF" : "#F1F1F1");
        SetBrush(resources, "AppMutedTextBrush", highContrast ? "#D3D3D3" : "#A7A7A7");
        SetBrush(resources, "AppAccentBrush", highContrast ? "#CBA6FF" : "#A371F7");
        SetBrush(resources, "AppAccentSoftBrush", highContrast ? "#4B3470" : "#3B2A55");
        SetBrush(resources, "AppHoverBrush", highContrast ? "#35303F" : "#333337");

        var scale = Math.Clamp(settings.Get(AppSettingKeys.UiScalePercent, 100), 80, 140) / 100d;
        if (settings.Get(AppSettingKeys.AccessibilityLargeText, false))
        {
            scale = Math.Max(scale, 1.12d);
        }

        resources["AppUiScale"] = scale;
        resources["AppBaseFontSize"] = Math.Round(13d * scale, 1);
        resources["AppLargeFontSize"] = Math.Round(16d * scale, 1);
        resources["AppTitleFontSize"] = Math.Round(22d * scale, 1);
        resources["AppAnimationsEnabled"] = settings.Get(AppSettingKeys.UiAnimation, true)
            && !settings.Get(AppSettingKeys.AccessibilityReducedMotion, false);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string colorText)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }
}
