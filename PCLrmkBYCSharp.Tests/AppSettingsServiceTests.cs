using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsKnownValues()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var settings = new AppSettingsService(paths);

        settings.Set(AppSettingKeys.LastRoute, PageRoute.Setup);
        settings.Set(AppSettingKeys.WindowWidth, 1280d);
        await settings.SaveAsync();

        var loaded = new AppSettingsService(paths);
        await loaded.LoadAsync();

        Assert.Equal(PageRoute.Setup, loaded.Get(AppSettingKeys.LastRoute, PageRoute.Launch));
        Assert.Equal(1280d, loaded.Get(AppSettingKeys.WindowWidth, 0d));
    }

    [Fact]
    public async Task InvalidJsonFallsBackToDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{ bad json");

        var settings = new AppSettingsService(paths);
        await settings.LoadAsync();

        Assert.Equal(PageRoute.Launch, settings.Get(AppSettingKeys.LastRoute, PageRoute.Launch));
    }

    [Fact]
    public void ResetAndHasSavedReflectCurrentValues()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));

        settings.Set(AppSettingKeys.Language, "zh-CN");
        Assert.True(settings.HasSaved(AppSettingKeys.Language));

        settings.Reset(AppSettingKeys.Language);
        Assert.False(settings.HasSaved(AppSettingKeys.Language));
    }

    [Fact]
    public void SettingChangedIsRaisedForSetAndReset()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var changedKeys = new List<string>();
        settings.SettingChanged += (_, args) => changedKeys.Add(args.Key);

        settings.Set(AppSettingKeys.Theme, "VS2022Dark");
        settings.Reset(AppSettingKeys.Theme);

        Assert.Equal([AppSettingKeys.Theme, AppSettingKeys.Theme], changedKeys);
    }

    [Fact]
    public void TypeConversionFailureReturnsDefaultValue()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));

        settings.Set(AppSettingKeys.WindowWidth, "not a number");

        Assert.Equal(1040d, settings.Get(AppSettingKeys.WindowWidth, 1040d));
    }
}
