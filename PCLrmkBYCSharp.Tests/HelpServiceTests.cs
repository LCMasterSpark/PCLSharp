using System.IO.Compression;
using System.Text;
using PCLrmkBYCSharp.Services;

namespace PCLrmkBYCSharp.Tests;

public sealed class HelpServiceTests
{
    [Fact]
    public void HelpTextExtractorReadsCommonOldPclHelpAttributes()
    {
        var text = HelpTextExtractor.Extract("""
        <local:MyCard Title="Intro">
          <TextBlock Text="Line one&#xa;Line two" />
          <local:MyListItem Title="Wiki" Info="Open details" />
          <!-- <TextBlock Text="Ignored" /> -->
        </local:MyCard>
        """);

        Assert.Contains("Intro", text, StringComparison.Ordinal);
        Assert.Contains("Line one", text, StringComparison.Ordinal);
        Assert.Contains("Line two", text, StringComparison.Ordinal);
        Assert.Contains("Wiki", text, StringComparison.Ordinal);
        Assert.Contains("Open details", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignored", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpDocumentParserReadsOldPclHelpBlocksActionsAndEventCollections()
    {
        var entry = new Models.HelpEntry(
            "Join Server",
            "Guide",
            "",
            ["Minecraft"],
            "Minecraft/join.json",
            false,
            "",
            "",
            """
            <local:MyCard Title="How to join?">
              <StackPanel>
                <TextBlock Text="Step one&#xa;Step two" />
                <local:MyHint Text="Remember this" IsWarn="False" />
                <local:MyListItem Title="Wiki" Info="Open docs" EventType="打开网页" EventData="https://example.com/wiki" />
                <local:MyButton Text="Copy" EventType="复制文本" EventData="abc" />
                <local:MyButton Text="Switch tutorial">
                  <local:CustomEventService.Events>
                    <local:CustomEventCollection>
                      <local:CustomEvent Type="修改变量" Data="TutorialVisibility1|Collapsed|-" />
                      <local:CustomEvent Type="刷新页面" Data="-" />
                    </local:CustomEventCollection>
                  </local:CustomEventService.Events>
                </local:MyButton>
              </StackPanel>
            </local:MyCard>
            """,
            true,
            true,
            true);

        var blocks = HelpDocumentParser.Parse(entry);

        Assert.Contains(blocks, block => block.Kind == "CardTitle" && block.Title == "How to join?");
        Assert.Contains(blocks, block => block.Kind == "Paragraph" && block.Text.Contains("Step one", StringComparison.Ordinal));
        Assert.Contains(blocks, block => block.Kind == "Hint" && block.Text == "Remember this");
        Assert.Contains(blocks, block => block.Kind == "ListItem" && block.Title == "Wiki" && block.EventType == "打开网页");
        Assert.Contains(blocks, block => block.Kind == "Action" && block.Title == "Copy" && block.EventType == "复制文本");
        var batch = Assert.Single(blocks, block => block.Kind == "Action" && block.Title == "Switch tutorial");
        Assert.Equal(2, batch.Events.Count);
        Assert.Equal("修改变量", batch.Events[0].EventType);
        Assert.Equal("刷新页面", batch.Events[1].EventType);
    }

    [Fact]
    public async Task HelpServiceLoadsBundledHelpZipAndSearchesEntries()
    {
        var service = new HelpService(new NullLoggerService());

        var entries = await service.LoadAsync();
        var results = service.Search(entries, "替换标记");

        Assert.True(entries.Count > 20);
        Assert.Contains(entries, entry => entry.RawPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, entry => entry.Title.Contains("替换标记", StringComparison.OrdinalIgnoreCase));
        Assert.All(results, entry => Assert.True(entry.ShowInSearch));
    }

    [Fact]
    public async Task HelpServiceLoadsCustomHelpAndAppliesHelpIgnoreToBundledEntries()
    {
        using var temp = new TempDirectory();
        var zipPath = Path.Combine(temp.Path, "Help.zip");
        CreateHelpZip(zipPath,
            ("Builtin/Keep.json", "Keep Builtin", "builtin keep"),
            ("Builtin/Ignored.json", "Ignored Builtin", "builtin ignored"));
        var customRoot = Path.Combine(temp.Path, "PCL", "Help");
        WriteCustomHelp(customRoot, "Custom/Custom.json", "Custom Help", "custom keyword");
        Directory.CreateDirectory(customRoot);
        await File.WriteAllTextAsync(Path.Combine(customRoot, "local.helpignore"), "Builtin/Ignored\\.json # hide old bundled entry");
        var service = new HelpService(new NullLoggerService(), zipPath, [customRoot]);

        var entries = await service.LoadAsync();
        var customResults = service.Search(entries, "custom");

        Assert.Contains(entries, entry => entry.Title == "Custom Help");
        Assert.Contains(entries, entry => entry.Title == "Keep Builtin");
        Assert.DoesNotContain(entries, entry => entry.Title == "Ignored Builtin");
        Assert.Single(customResults);
    }

    [Fact]
    public async Task HelpServiceReloadsCustomHelpFromDisk()
    {
        using var temp = new TempDirectory();
        var zipPath = Path.Combine(temp.Path, "Help.zip");
        CreateHelpZip(zipPath, ("Builtin/Keep.json", "Keep Builtin", "builtin keep"));
        var customRoot = Path.Combine(temp.Path, "Help");
        WriteCustomHelp(customRoot, "Custom/One.json", "One Help", "one");
        var service = new HelpService(new NullLoggerService(), zipPath, [customRoot]);

        Assert.Contains(await service.LoadAsync(), entry => entry.Title == "One Help");

        WriteCustomHelp(customRoot, "Custom/Two.json", "Two Help", "two");
        var reloaded = await service.LoadAsync();

        Assert.Contains(reloaded, entry => entry.Title == "Two Help");
    }

    private static void CreateHelpZip(string path, params (string JsonPath, string Title, string Keywords)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            AddZipText(archive, entry.JsonPath, CreateHelpJson(entry.Title, entry.Keywords));
            AddZipText(archive, Path.ChangeExtension(entry.JsonPath, ".xaml")!.Replace('\\', '/'), "<TextBlock Text=\"Help\" />");
        }
    }

    private static void WriteCustomHelp(string root, string relativeJsonPath, string title, string keywords)
    {
        var jsonPath = Path.Combine(root, relativeJsonPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, CreateHelpJson(title, keywords));
        File.WriteAllText(Path.ChangeExtension(jsonPath, ".xaml"), "<TextBlock Text=\"Custom\" />");
    }

    private static string CreateHelpJson(string title, string keywords)
    {
        return $$"""
        {
          "Title": "{{title}}",
          "Description": "Description for {{title}}",
          "Keywords": "{{keywords}}",
          "Types": ["Guide"]
        }
        """;
    }

    private static void AddZipText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }
}
