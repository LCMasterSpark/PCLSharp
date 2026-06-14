using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;
using PCLrmkBYCSharp.Services.FeatureHub;
using PCLrmkBYCSharp.Services.Launch;
using PCLrmkBYCSharp.ViewModels;

namespace PCLrmkBYCSharp.Tests;

public sealed class OtherPageViewModelTests
{
    [Fact]
    public async Task OtherPageShowsDiagnosticsAboutAndSearchableHelpInformation()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);

        var actions = new FakeHelpActionService();
        var helpService = new FakeHelpService();
        var folders = new CaptureFolderOpenService();
        var clipboard = new CaptureClipboardService();
        var reportPath = System.IO.Path.Combine(temp.Path, ".minecraft", "crash-reports", "crash-test.txt");
        var featureHub = new FakeFeatureHubService(reportPath);
        var files = new CaptureFileOpenService();
        var viewModel = new OtherPageViewModel(paths, helpService, new NullLoggerService(), actions, featureHub: featureHub, folders: folders, files: files, clipboard: clipboard);

        await viewModel.OnNavigatedToAsync();
        viewModel.HelpSearchText = "marker";

        Assert.Equal(PageRoute.Other, viewModel.Route);
        Assert.Contains(viewModel.Cards, card => card.Description.Contains(paths.LogsDirectory, StringComparison.Ordinal));
        Assert.Contains(viewModel.Cards, card => card.Description.Contains(paths.SettingsFilePath, StringComparison.Ordinal));
        Assert.Contains(viewModel.Cards, card => card.Description.Contains(".NET", StringComparison.Ordinal));
        Assert.Contains(viewModel.Cards, card => card.Description.Contains("PCL Sharp", StringComparison.Ordinal));
        Assert.Contains(viewModel.AboutLinks, link => link.Title == "Plain Craft Launcher Sharp" && link.Description.Contains("实验性 C# WPF 重构版", StringComparison.Ordinal));
        Assert.Contains(viewModel.AboutLinks, link => link.Title == "原版 PCL" && link.Url == "https://github.com/Hex-Dragon/PCL2");
        Assert.Contains(viewModel.AboutLinks, link => link.Title == "重构说明" && link.Description.Contains("功能一比一还原", StringComparison.Ordinal));
        Assert.Contains(viewModel.AboutLinks, link => link.Title == "MC 百科" && link.Url == "https://www.mcmod.cn");
        Assert.Contains(viewModel.OtherSections, section => section.DisplayName == "百宝箱");
        Assert.Contains(viewModel.OtherSections, section => section.DisplayName == "反馈");
        Assert.Contains(viewModel.ToolBoxItems, item => item.Title == "今日人品");
        Assert.Contains(viewModel.ToolBoxItems, item => item.Title == "下载自定义文件");

        viewModel.OpenLogsFolderCommand.Execute(null);

        Assert.Equal(paths.LogsDirectory, folders.OpenedFolders.Last());
        Assert.Contains("已打开日志目录", viewModel.DiagnosticsStatusText, StringComparison.Ordinal);

        viewModel.OpenSettingsFolderCommand.Execute(null);

        Assert.Equal(System.IO.Path.GetDirectoryName(paths.SettingsFilePath), folders.OpenedFolders.Last());
        Assert.Contains("已打开设置目录", viewModel.DiagnosticsStatusText, StringComparison.Ordinal);

        viewModel.CopyDiagnosticsCommand.Execute(null);

        Assert.Contains("Plain Craft Launcher Sharp 诊断信息", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains(paths.LogsDirectory, clipboard.Text, StringComparison.Ordinal);
        Assert.Contains(paths.SettingsFilePath, clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("诊断信息已复制", viewModel.DiagnosticsStatusText, StringComparison.Ordinal);

        viewModel.OpenCrashReportFolderCommand.Execute(null);

        Assert.Equal(System.IO.Path.GetDirectoryName(reportPath), folders.OpenedFolders.Last());
        Assert.Contains("已打开报告目录", viewModel.CrashAnalysisText, StringComparison.Ordinal);

        viewModel.OpenCrashReportFileCommand.Execute(null);

        Assert.Equal(reportPath, files.LastFile);
        Assert.Contains("已打开报告文件", viewModel.CrashAnalysisText, StringComparison.Ordinal);

        viewModel.CopyCrashAnalysisCommand.Execute(null);

        Assert.Contains("疑似原因：Java 版本过旧", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains(reportPath, clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("崩溃诊断已复制", viewModel.CrashAnalysisText, StringComparison.Ordinal);

        viewModel.CopyAccountSummaryCommand.Execute(null);

        Assert.Contains("Plain Craft Launcher Sharp 账号摘要", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("当前登录：Legacy", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("显示名称：Steve", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("缓存账号：0", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("账号摘要已复制", viewModel.AccountCenterText, StringComparison.Ordinal);

        viewModel.CopySkinSummaryCommand.Execute(null);

        Assert.Contains("Plain Craft Launcher Sharp 皮肤摘要", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("皮肤模式：随机", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("皮肤标识：未指定", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("Slim：否", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("皮肤摘要已复制", viewModel.SkinCenterText, StringComparison.Ordinal);

        viewModel.CopyExtensionCatalogCommand.Execute(null);

        Assert.Contains("Plain Craft Launcher Sharp 扩展点目录", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("联机后端", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("基础接入", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("诊断规则", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("已复制 2 个扩展点目录项", viewModel.ExtensionPointText, StringComparison.Ordinal);

        var help = Assert.Single(viewModel.HelpResults);
        Assert.Equal("marker help", help.Title);
        Assert.Contains("marker help", viewModel.SelectedHelpPreview, StringComparison.Ordinal);
        Assert.Contains("Readable help body", viewModel.SelectedHelpPreview, StringComparison.Ordinal);
        Assert.Contains(viewModel.SelectedHelpDocumentBlocks, block => block.Kind == "CardTitle" && block.Title == "Readable card");
        var actionBlock = Assert.Single(viewModel.SelectedHelpDocumentBlocks, block => block.Kind == "Action" && block.EventType == "复制文本");
        var openHelpBlock = Assert.Single(viewModel.SelectedHelpDocumentBlocks, block => block.Kind == "Action" && block.EventType == "打开帮助");
        var refreshBlock = Assert.Single(viewModel.SelectedHelpDocumentBlocks, block => block.Kind == "Action" && block.EventType == "刷新帮助");
        var refreshPageBlock = Assert.Single(viewModel.SelectedHelpDocumentBlocks, block => block.Kind == "Action" && block.EventType == "刷新页面");
        var batchBlock = Assert.Single(viewModel.SelectedHelpDocumentBlocks, block => block.Kind == "Action" && block.Title == "Batch");

        await viewModel.OpenSelectedHelpCommand.ExecuteAsync(null);

        Assert.Equal(help, actions.LastEntry);
        Assert.Contains("opened marker help", viewModel.HelpActionStatusText, StringComparison.Ordinal);

        await viewModel.ExecuteHelpDocumentBlockCommand.ExecuteAsync(actionBlock);

        Assert.Equal("复制文本", actions.LastEntry?.EventType);
        Assert.Equal("from block", actions.LastEntry?.EventData);

        await viewModel.ExecuteHelpDocumentBlockCommand.ExecuteAsync(openHelpBlock);

        Assert.Equal("second help", viewModel.SelectedHelpEntry?.Title);
        Assert.Contains("已打开帮助：second help", viewModel.HelpActionStatusText, StringComparison.Ordinal);

        await viewModel.ExecuteHelpDocumentBlockCommand.ExecuteAsync(refreshBlock);

        Assert.True(helpService.LoadCount >= 2);
        Assert.Contains("已刷新帮助列表", viewModel.HelpActionStatusText, StringComparison.Ordinal);

        await viewModel.ExecuteHelpDocumentBlockCommand.ExecuteAsync(refreshPageBlock);

        Assert.True(helpService.LoadCount >= 3);
        Assert.Contains("已刷新当前页面", viewModel.HelpActionStatusText, StringComparison.Ordinal);

        await viewModel.ExecuteHelpDocumentBlockCommand.ExecuteAsync(batchBlock);

        Assert.Contains(actions.Entries, entry => entry.EventType == "修改变量" && entry.EventData == "TutorialVisibility1|Collapsed|-");
        Assert.Contains("已执行 2 个页面事件", viewModel.HelpActionStatusText, StringComparison.Ordinal);

        actions.LastEntry = null;
        viewModel.HelpSearchText = "event opener";
        await viewModel.OpenSelectedHelpCommand.ExecuteAsync(null);

        Assert.Equal("second help", viewModel.SelectedHelpEntry?.Title);
        Assert.Null(actions.LastEntry);
        Assert.Contains("已打开帮助：second help", viewModel.HelpActionStatusText, StringComparison.Ordinal);

        var sourceLink = viewModel.AboutLinks.Single(link => link.Title == "原版 PCL");
        await viewModel.OpenAboutLinkCommand.ExecuteAsync(sourceLink);

        Assert.Equal(sourceLink.Url, actions.LastEntry?.EventData);
        Assert.Contains("opened 原版 PCL", viewModel.AboutActionStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolboxCommandsProvideRealActionsAndUseDownloadQueue()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var minecraftRoot = System.IO.Path.Combine(temp.Path, ".minecraft");
        Directory.CreateDirectory(System.IO.Path.Combine(minecraftRoot, "logs"));
        Directory.CreateDirectory(System.IO.Path.Combine(minecraftRoot, "versions", "demo", "demo-natives"));
        var oldLog = System.IO.Path.Combine(minecraftRoot, "logs", "old.log.gz");
        var nativeFile = System.IO.Path.Combine(minecraftRoot, "versions", "demo", "demo-natives", "native.tmp");
        await File.WriteAllTextAsync(oldLog, "old log");
        await File.WriteAllTextAsync(nativeFile, "native");
        File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-2));

        var settings = new FakeSettingsService();
        settings.Set(AppSettingKeys.MinecraftRootPath, minecraftRoot);
        var selectedFolder = System.IO.Path.Combine(temp.Path, "downloads");
        var fileDialogs = new FolderDialogService(selectedFolder);
        var downloads = new CaptureDownloadManager();
        var memory = new FakeMemoryOptimizer();
        var viewModel = new OtherPageViewModel(paths, logger: new NullLoggerService(), settings: settings, fileDialogs: fileDialogs, downloadManager: downloads, memoryOptimizer: memory);

        viewModel.RollLuckCommand.Execute(null);

        Assert.Contains("今日人品", viewModel.ToolboxStatusText, StringComparison.Ordinal);

        await viewModel.OptimizeMemoryCommand.ExecuteAsync(null);

        Assert.Equal(1, memory.CallCount);
        Assert.Contains("已处理进程数：7", viewModel.ToolboxStatusText, StringComparison.Ordinal);

        viewModel.CleanGameTrashCommand.Execute(null);

        Assert.False(File.Exists(oldLog));
        Assert.False(Directory.Exists(System.IO.Path.Combine(minecraftRoot, "versions", "demo", "demo-natives")));
        Assert.Contains("已清理", viewModel.ToolboxStatusText, StringComparison.Ordinal);

        viewModel.BrowseCustomDownloadFolderCommand.Execute(null);
        viewModel.CustomDownloadUrl = "https://example.invalid/files/demo.zip";

        Assert.Equal(selectedFolder, viewModel.CustomDownloadFolder);
        Assert.Equal("demo.zip", viewModel.CustomDownloadFileName);

        await viewModel.StartCustomDownloadCommand.ExecuteAsync(null);

        Assert.Equal("自定义下载：demo.zip", downloads.LastName);
        var file = Assert.Single(downloads.LastFiles);
        Assert.Equal("https://example.invalid/files/demo.zip", Assert.Single(file.Sources));
        Assert.Equal(System.IO.Path.Combine(selectedFolder, "demo.zip"), file.LocalPath);
        Assert.Contains("下载完成", viewModel.CustomDownloadStatusText, StringComparison.Ordinal);
    }

    private sealed class FakeHelpService : IHelpService
    {
        private static readonly IReadOnlyList<HelpEntry> Entries =
        [
            new("marker help", "dynamic text in launch arguments", "marker variables", ["custom"], "custom/marker.json", false, "", "", """
            <local:MyCard Title="Readable card">
              <TextBlock Text="Readable help body" />
              <local:MyButton Text="Copy marker" EventType="复制文本" EventData="from block" />
              <local:MyButton Text="Open other help" EventType="打开帮助" EventData="second help" />
              <local:MyButton Text="Refresh help" EventType="刷新帮助" />
              <local:MyButton Text="Refresh page" EventType="刷新页面" EventData="-" />
              <local:MyButton Text="Batch">
                <local:CustomEventService.Events>
                  <local:CustomEventCollection>
                    <local:CustomEvent Type="修改变量" Data="TutorialVisibility1|Collapsed|-" />
                    <local:CustomEvent Type="刷新页面" Data="-" />
                  </local:CustomEventCollection>
                </local:CustomEventService.Events>
              </local:MyButton>
            </local:MyCard>
            """, true, true, true),
            new("event opener", "opens another help directly", "", ["help"], "help/event.json", true, "打开帮助", "second help", "", true, true, true),
            new("second help", "another searchable entry", "", ["help"], "help/second.json", false, "", "", "<TextBlock Text=\"Second body\" />", true, true, true),
            new("hidden help", "not searchable", "", ["help"], "help/hidden.json", false, "", "", "<TextBlock />", false, true, true)
        ];

        public int LoadCount { get; private set; }

        public Task<IReadOnlyList<HelpEntry>> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(Entries);
        }

        public IReadOnlyList<HelpEntry> Search(IReadOnlyList<HelpEntry> entries, string query, int maxCount = 30)
        {
            return entries
                .Where(entry => entry.ShowInSearch && entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(maxCount)
                .ToList();
        }
    }

    private sealed class FakeHelpActionService : IHelpActionService
    {
        public List<HelpEntry> Entries { get; } = [];

        public HelpEntry? LastEntry { get; set; }

        public Task<HelpActionResult> ExecuteAsync(HelpEntry entry, CancellationToken cancellationToken = default)
        {
            LastEntry = entry;
            Entries.Add(entry);
            return Task.FromResult(new HelpActionResult(true, "opened " + entry.Title));
        }

        public void SetEventHandler(string eventType, Func<string, CancellationToken, Task<HelpActionResult>> handler)
        {
        }
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<AppSettingChangedEventArgs>? SettingChanged;

        public T Get<T>(string key) => Get<T>(key, default!);

        public T Get<T>(string key, T defaultValue)
        {
            return _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            _values[key] = value;
            SettingChanged?.Invoke(this, new AppSettingChangedEventArgs(key, value));
        }

        public void Reset(string key)
        {
            _values.Remove(key);
        }

        public bool HasSaved(string key) => _values.ContainsKey(key);

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FolderDialogService(string folder) : IFileDialogService
    {
        public string? PickFolder(string title, string initialDirectory) => folder;

        public string? PickJavaExecutable(string initialDirectory) => null;

        public string? PickSkinFile(string initialDirectory) => null;

        public string? PickModpackFile(string initialDirectory) => null;

        public IReadOnlyList<string> PickModFiles(string initialDirectory) => [];

        public string? PickSaveFile(string title, string initialDirectory, string defaultFileName, string filter) => null;
    }

    private sealed class CaptureDownloadManager : IDownloadManagerService
    {
        public event EventHandler<DownloadTaskSnapshot>? SnapshotChanged;

        public IReadOnlyList<DownloadTaskSnapshot> Tasks => [];

        public string LastName { get; private set; } = "";

        public IReadOnlyList<DownloadFile> LastFiles { get; private set; } = [];

        public Task<DownloadTaskSnapshot> DownloadAsync(string name, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken = default)
        {
            LastName = name;
            LastFiles = files;
            var snapshot = new DownloadTaskSnapshot(name, DownloadTaskState.Succeeded, files.Count, files.Count, 10, 1, "下载完成");
            SnapshotChanged?.Invoke(this, snapshot);
            return Task.FromResult(snapshot);
        }

        public bool Cancel(string name) => false;

        public int CancelAllRunning() => 0;

        public Task<DownloadTaskSnapshot?> RetryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<DownloadTaskSnapshot?>(null);

        public int ClearFinished() => 0;
    }

    private sealed class FakeMemoryOptimizer : ILaunchMemoryOptimizer
    {
        public int CallCount { get; private set; }

        public Task<LaunchMemoryOptimizeResult> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new LaunchMemoryOptimizeResult(7));
        }
    }

    private sealed class FakeFeatureHubService(string reportPath) : IFeatureHubService
    {
        public IReadOnlyList<FeatureModuleSnapshot> GetModules() => [];

        public IReadOnlyList<HomeFeedItem> GetHomeFeedItems() => [];

        public CrashAnalysisSummary AnalyzeCrashes()
        {
            return new CrashAnalysisSummary(
                "发现最近崩溃报告，已完成基础规则分析",
                reportPath,
                DateTimeOffset.Now,
                1,
                "Initializing game",
                "Java 版本过旧",
                "切换到该 Minecraft / 加载器要求的 Java 版本后重试。");
        }

        public AccountCenterSummary GetAccountSummary() => new("账号中心入口已预留", "Legacy", "Steve", 0);

        public SkinCenterSummary GetSkinSummary() => new("读取设置", "随机", "未指定", false);

        public IReadOnlyList<ExtensionPointInfo> GetExtensionPoints() =>
        [
            new("联机后端", "记录联机后端启动、连接和日志扩展点。", "基础接入"),
            new("诊断规则", "记录崩溃报告和启动日志规则扩展点。", "目录占位")
        ];
    }

    private sealed class CaptureFolderOpenService : IFolderOpenService
    {
        public List<string> OpenedFolders { get; } = [];

        public void OpenFolder(string folderPath)
        {
            OpenedFolders.Add(folderPath);
        }
    }

    private sealed class CaptureFileOpenService : IFileOpenService
    {
        public string LastFile { get; private set; } = "";

        public void OpenFile(string filePath)
        {
            LastFile = filePath;
        }
    }

    private sealed class CaptureClipboardService : IClipboardService
    {
        public string Text { get; private set; } = "";

        public void SetText(string text)
        {
            Text = text;
        }
    }
}
