using System.IO;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.FeatureHub;

namespace PCLrmkBYCSharp.Tests;

public sealed class FeatureHubServiceTests
{
    [Fact]
    public void FeatureHubServiceReportsPlannedModules()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        var service = new FeatureHubService(new TestAppPathService(temp.Path), settings);

        var modules = service.GetModules();

        Assert.Contains(modules, module => module.Title == "更新系统");
        Assert.Contains(modules, module => module.Title == "崩溃分析");
        Assert.Contains(modules, module => module.Title == "账号管理中心");
        Assert.Contains(modules, module => module.Title == "扩展点");
    }

    [Fact]
    public void FeatureHubServiceReadsAccountAndSkinSettings()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var settings = new AppSettingsService(paths);
        settings.Set(AppSettingKeys.LoginType, LoginType.Ms);
        settings.Set(AppSettingKeys.CacheMsV2Name, "Alex");
        settings.Set(AppSettingKeys.LoginMsJson, "{\"one\":{},\"two\":{}}");
        settings.Set(AppSettingKeys.LaunchSkinType, 3);
        settings.Set(AppSettingKeys.LaunchSkinID, "Notch");
        settings.Set(AppSettingKeys.LaunchSkinSlim, true);
        var service = new FeatureHubService(paths, settings);

        var account = service.GetAccountSummary();
        var skin = service.GetSkinSummary();

        Assert.Equal("Ms", account.CurrentLoginType);
        Assert.Equal("Alex", account.CurrentDisplayName);
        Assert.Equal(2, account.CachedAccountCount);
        Assert.Equal("正版皮肤", skin.SkinMode);
        Assert.Equal("Notch", skin.SkinIdentity);
        Assert.True(skin.SlimModel);
    }

    [Fact]
    public void FeatureHubServiceFindsLatestCrashReportAndDiagnosesJavaMismatch()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var settings = new AppSettingsService(paths);
        var minecraft = Path.Combine(temp.Path, ".minecraft");
        var crashReports = Path.Combine(minecraft, "crash-reports");
        Directory.CreateDirectory(crashReports);
        var oldReport = Path.Combine(crashReports, "crash-old.txt");
        var newReport = Path.Combine(crashReports, "crash-new.txt");
        File.WriteAllText(oldReport, "old");
        File.WriteAllText(newReport, """
---- Minecraft Crash Report ----
Description: Initializing game

java.lang.UnsupportedClassVersionError: net/example/ExampleMod has been compiled by a more recent version of the Java Runtime
""");
        File.SetLastWriteTimeUtc(oldReport, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(newReport, DateTime.UtcNow.AddDays(-1));
        settings.Set(AppSettingKeys.MinecraftRootPath, minecraft);
        var service = new FeatureHubService(paths, settings);

        var summary = service.AnalyzeCrashes();

        Assert.Equal(2, summary.ReportCount);
        Assert.Equal(newReport, summary.LatestReportPath);
        Assert.Contains("发现", summary.Status, StringComparison.Ordinal);
        Assert.Equal("Initializing game", summary.Title);
        Assert.Equal("Java 版本过旧", summary.SuspectedCause);
        Assert.Contains("切换", summary.Suggestion, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("java.lang.OutOfMemoryError: Java heap space", "内存不足")]
    [InlineData("org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError", "Mixin 注入失败")]
    [InlineData("net.minecraftforge.fml.loading.DuplicateModsFoundException: duplicate mod", "重复 Mod")]
    [InlineData("java.lang.NoClassDefFoundError: com/example/Required", "缺少类或依赖")]
    public void FeatureHubServiceMatchesCommonCrashRules(string errorLine, string expectedCause)
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var settings = new AppSettingsService(paths);
        var minecraft = Path.Combine(temp.Path, ".minecraft");
        var crashReports = Path.Combine(minecraft, "crash-reports");
        Directory.CreateDirectory(crashReports);
        var report = Path.Combine(crashReports, "crash-rule.txt");
        File.WriteAllText(report, "Description: Rendering screen" + Environment.NewLine + errorLine);
        settings.Set(AppSettingKeys.MinecraftRootPath, minecraft);
        var service = new FeatureHubService(paths, settings);

        var summary = service.AnalyzeCrashes();

        Assert.Equal("Rendering screen", summary.Title);
        Assert.Equal(expectedCause, summary.SuspectedCause);
        Assert.False(string.IsNullOrWhiteSpace(summary.Suggestion));
    }

    [Fact]
    public void FeatureHubServiceProvidesHomeFeedAndExtensionCatalog()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var service = new FeatureHubService(paths, new AppSettingsService(paths));

        Assert.NotEmpty(service.GetHomeFeedItems());
        Assert.Contains(service.GetExtensionPoints(), item => item.Name == "联机后端");
    }
}
