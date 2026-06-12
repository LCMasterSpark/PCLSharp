using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class HelpActionServiceTests
{
    [Fact]
    public async Task HelpActionServiceOpensHttpWebsiteEvents()
    {
        Uri? opened = null;
        var service = new HelpActionService(
            (uri, _) =>
            {
                opened = uri;
                return Task.CompletedTask;
            });
        var entry = CreateEvent("打开网页", "https://example.com/help");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal("https://example.com/help", opened?.ToString().TrimEnd('/'));
        Assert.Contains("已打开网页", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceRejectsUnsafeWebsiteEvents()
    {
        var service = new HelpActionService((_, _) => throw new InvalidOperationException("Should not open."));
        var entry = CreateEvent("打开网页", "file:///C:/secret.txt");

        var result = await service.ExecuteAsync(entry);

        Assert.False(result.Success);
        Assert.Contains("无效", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceShowsMessageBoxEvents()
    {
        string? title = null;
        string? message = null;
        var service = new HelpActionService(
            showMessage: (t, m) =>
            {
                title = t;
                message = m;
            });
        var entry = CreateEvent("弹出窗口", "Title|Line 1\\nLine 2");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal("Title", title);
        Assert.Equal($"Line 1{Environment.NewLine}Line 2", message);
        Assert.Contains("已显示帮助提示", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceCopiesTextEvents()
    {
        string? copied = null;
        var service = new HelpActionService(setClipboardText: text => copied = text);
        var entry = CreateEvent("复制文本", "hello pcl");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal("hello pcl", copied);
        Assert.Contains("已复制文本", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceShowsHintEvents()
    {
        string? message = null;
        string? hintType = null;
        var service = new HelpActionService(showHint: (text, type) =>
        {
            message = text;
            hintType = type;
        });
        var entry = CreateEvent("弹出提示", "Line 1\\nLine 2|Green");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal($"Line 1{Environment.NewLine}Line 2", message);
        Assert.Equal("Green", hintType);
    }

    [Fact]
    public async Task HelpActionServiceStartsOpenFileAndCommandEvents()
    {
        var calls = new List<(string FileName, string Arguments, string? WorkingDirectory)>();
        var service = new HelpActionService(startProcess: (file, arguments, workingDirectory, _) =>
        {
            calls.Add((file, arguments, workingDirectory));
            return Task.CompletedTask;
        });

        var openFile = await service.ExecuteAsync(CreateEvent("打开文件", "C:\\Tools\\tool.exe|--open|C:\\Tools"));
        var command = await service.ExecuteAsync(CreateEvent("执行命令", "cmd.exe|/c echo ok"));

        Assert.True(openFile.Success);
        Assert.True(command.Success);
        Assert.Equal(("C:\\Tools\\tool.exe", "--open", "C:\\Tools"), calls[0]);
        Assert.Equal(("cmd.exe", "/c echo ok", null), calls[1]);
    }

    [Fact]
    public async Task HelpActionServiceWritesSettingEvents()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        await settings.LoadAsync();
        var service = new HelpActionService(settings: settings);
        var entry = CreateEvent("写入设置", $"{AppSettingKeys.LaunchArgumentInfo}|PCL Sharp\\nCustom");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal($"PCL Sharp{Environment.NewLine}Custom", settings.Get(AppSettingKeys.LaunchArgumentInfo, ""));
        Assert.True(settings.HasSaved(AppSettingKeys.LaunchArgumentInfo));
    }

    [Fact]
    public async Task HelpActionServiceWritesCustomVariableEvents()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettingsService(new TestAppPathService(temp.Path));
        await settings.LoadAsync();
        var service = new HelpActionService(settings: settings);
        var entry = CreateEvent("修改变量", "TutorialVisibility1|Collapsed|-");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal("Collapsed", settings.Get("CustomEvent.TutorialVisibility1", ""));
        Assert.True(settings.HasSaved("CustomEvent.TutorialVisibility1"));
    }

    [Fact]
    public async Task HelpActionServiceDownloadsFileEvents()
    {
        Uri? downloaded = null;
        string? fileName = null;
        var service = new HelpActionService(downloadFile: (uri, name, _) =>
        {
            downloaded = uri;
            fileName = name;
            return Task.CompletedTask;
        });
        var entry = CreateEvent("下载文件", "https://example.com/logo.png|logo.png");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal("https://example.com/logo.png", downloaded?.ToString());
        Assert.Equal("logo.png", fileName);
    }

    [Fact]
    public async Task HelpActionServiceRunsMaintenanceEvents()
    {
        var hints = new List<string>();
        var service = new HelpActionService(showHint: (message, _) => hints.Add(message));

        var memory = await service.ExecuteAsync(CreateEvent("内存优化", ""));
        var rubbish = await service.ExecuteAsync(CreateEvent("清理垃圾", ""));
        var jrrp = await service.ExecuteAsync(CreateEvent("今日人品", ""));

        Assert.True(memory.Success);
        Assert.True(rubbish.Success);
        Assert.True(jrrp.Success);
        Assert.Contains(hints, item => item.StartsWith("今日人品：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HelpActionServiceAcceptsOldPclRefreshPageEvent()
    {
        var service = new HelpActionService();

        var result = await service.ExecuteAsync(CreateEvent("刷新页面", "-"));

        Assert.True(result.Success);
        Assert.Contains("刷新页面", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceExecutesBundledChineseWebsiteEvent()
    {
        Uri? opened = null;
        var service = new HelpActionService(
            (uri, _) =>
            {
                opened = uri;
                return Task.CompletedTask;
            });
        var help = new HelpService(new NullLoggerService());
        var entries = await help.LoadAsync();
        var entry = entries.Single(item => item.Title == "Minecraft 新手指南");

        var result = await service.ExecuteAsync(entry);

        Assert.True(result.Success);
        Assert.Equal("打开网页", entry.EventType);
        Assert.NotNull(opened);
        Assert.Equal("zh.minecraft.wiki", opened!.Host);
    }

    [Fact]
    public async Task HelpActionServiceReportsLaunchGameRequiresLaunchPage()
    {
        var service = new HelpActionService();
        var entry = CreateEvent("启动游戏", "\\current");

        var result = await service.ExecuteAsync(entry);

        Assert.False(result.Success);
        Assert.Contains("启动页", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceDelegatesLaunchGameEvent()
    {
        var launched = "";
        var service = new HelpActionService();
        service.SetLaunchGameHandler((eventData, _) =>
        {
            launched = eventData;
            return Task.FromResult(new HelpActionResult(true, "已启动"));
        });

        var result = await service.ExecuteAsync(CreateEvent("启动游戏", "1.20.1|mc.hypixel.net"));

        Assert.True(result.Success);
        Assert.Equal("1.20.1|mc.hypixel.net", launched);
        Assert.Equal("已启动", result.Message);
    }

    [Theory]
    [InlineData("刷新主页", "启动页")]
    [InlineData("切换页面", "主窗口导航")]
    [InlineData("导入整合包", "下载页")]
    [InlineData("安装整合包", "下载页")]
    [InlineData("加入房间", "联机页面")]
    [InlineData("检查更新", "更新服务")]
    public async Task HelpActionServiceRecognizesOldPclUiEventsBeforePageHandlersAreConnected(string eventType, string expectedScope)
    {
        var service = new HelpActionService();

        var result = await service.ExecuteAsync(CreateEvent(eventType, "payload"));

        Assert.False(result.Success);
        Assert.Contains(expectedScope, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("暂不支持", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Launch", PageRoute.Launch)]
    [InlineData("Download|DownloadMod", PageRoute.Download)]
    [InlineData("6", PageRoute.Download)]
    [InlineData("InstanceSelect", PageRoute.Instance)]
    [InlineData("7", PageRoute.Instance)]
    [InlineData("Setup", PageRoute.Setup)]
    [InlineData("Other", PageRoute.Other)]
    public async Task HelpActionServiceSwitchesOldPclPagesWhenNavigationIsConnected(string eventData, PageRoute expectedRoute)
    {
        PageRoute? navigated = null;
        var service = new HelpActionService(switchPage: (route, _) =>
        {
            navigated = route;
            return Task.CompletedTask;
        });

        var result = await service.ExecuteAsync(CreateEvent("切换页面", eventData));

        Assert.True(result.Success);
        Assert.Equal(expectedRoute, navigated);
        Assert.Contains("已切换页面", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceReportsMissingMigratedPageForOldPclLinkPage()
    {
        var service = new HelpActionService(switchPage: (_, _) => throw new InvalidOperationException("Should not navigate."));

        var result = await service.ExecuteAsync(CreateEvent("切换页面", "Link"));

        Assert.False(result.Success);
        Assert.Contains("联机页面尚未迁移", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpActionServiceDispatchesOldPclUiEventsToRegisteredHandlers()
    {
        var calls = new List<(string EventType, string EventData)>();
        var service = new HelpActionService();
        var eventTypes = new[]
        {
            "刷新主页",
            "切换页面",
            "导入整合包",
            "安装整合包",
            "加入房间",
            "检查更新",
            "刷新帮助",
            "打开帮助"
        };
        foreach (var eventType in eventTypes)
        {
            service.SetEventHandler(eventType, (eventData, _) =>
            {
                calls.Add((eventType, eventData));
                return Task.FromResult(new HelpActionResult(true, "handled " + eventType));
            });
        }

        foreach (var eventType in eventTypes)
        {
            var result = await service.ExecuteAsync(CreateEvent(eventType, eventType + "|data"));
            Assert.True(result.Success);
        }

        Assert.Equal(8, calls.Count);
        Assert.Contains(calls, item => item == ("切换页面", "切换页面|data"));
        Assert.Contains(calls, item => item == ("打开帮助", "打开帮助|data"));
    }

    [Fact]
    public void HelpActionServiceParsesModpackDownloadPreset()
    {
        var preset = HelpActionService.ParseModpackDownloadPreset("Create Above and Beyond|1.18.2|Forge");

        Assert.Equal("Create Above and Beyond", preset.SearchText);
        Assert.Equal("1.18.2", preset.GameVersion);
        Assert.Equal("Forge", preset.Loader);
    }

    [Theory]
    [InlineData("导入整合包")]
    [InlineData("安装整合包")]
    public async Task HelpActionServiceRunsConnectedModpackEvents(string eventType)
    {
        HelpActionService.ModpackDownloadPreset? preset = null;
        var service = new HelpActionService();
        service.SetEventHandler(eventType, (eventData, _) =>
        {
            preset = HelpActionService.ParseModpackDownloadPreset(eventData);
            return Task.FromResult(new HelpActionResult(true, "已打开整合包入口：" + preset.SearchText));
        });

        var result = await service.ExecuteAsync(CreateEvent(eventType, "SkyFactory|1.20.1|Fabric"));

        Assert.True(result.Success);
        Assert.Equal("SkyFactory", preset?.SearchText);
        Assert.Equal("1.20.1", preset?.GameVersion);
        Assert.Equal("Fabric", preset?.Loader);
        Assert.DoesNotContain("需要接入下载页", result.Message, StringComparison.Ordinal);
    }

    private static HelpEntry CreateEvent(string eventType, string eventData)
    {
        return new HelpEntry("event", "", "", [], "event.json", true, eventType, eventData, "", true, true, true);
    }
}
