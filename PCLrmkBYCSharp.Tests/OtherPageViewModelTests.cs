using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
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
        var viewModel = new OtherPageViewModel(paths, helpService, new NullLoggerService(), actions);

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
        Assert.Contains(viewModel.ToolBoxItems, item => item.Title == "日志与诊断");
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
}
