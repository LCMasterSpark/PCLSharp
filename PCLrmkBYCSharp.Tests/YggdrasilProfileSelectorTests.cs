using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.Tests;

public sealed class YggdrasilProfileSelectorTests
{
    [Fact]
    public async Task WpfYggdrasilProfileSelectorUsesApplicationPromptChoices()
    {
        var prompts = new CapturePromptService(selectedIndex: 0);
        var selector = new WpfYggdrasilProfileSelector(prompts);
        var profiles = new[]
        {
            new YggdrasilProfileOption("uuid-alex", "Alex"),
            new YggdrasilProfileOption("uuid-steve", "Steve")
        };

        var selected = await selector.SelectAsync("选择角色", profiles, "Steve");

        Assert.NotNull(selected);
        Assert.Equal("Alex", selected.Name);
        Assert.Equal("选择角色", prompts.Title);
        Assert.Equal(1, prompts.DefaultIndex);
        Assert.Collection(
            prompts.Options,
            first => Assert.Contains("Alex", first, StringComparison.Ordinal),
            second => Assert.Contains("Steve", second, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WpfYggdrasilProfileSelectorReturnsNullWhenPromptIsCanceled()
    {
        var selector = new WpfYggdrasilProfileSelector(new CapturePromptService(selectedIndex: null));
        var profiles = new[]
        {
            new YggdrasilProfileOption("uuid-alex", "Alex"),
            new YggdrasilProfileOption("uuid-steve", "Steve")
        };

        var selected = await selector.SelectAsync("选择角色", profiles, "");

        Assert.Null(selected);
    }

    private sealed class CapturePromptService(int? selectedIndex) : IUserPromptService
    {
        public string Title { get; private set; } = "";

        public int DefaultIndex { get; private set; } = -1;

        public IReadOnlyList<string> Options { get; private set; } = [];

        public bool Confirm(string title, string message) => false;

        public string? Prompt(string title, string message, string defaultValue) => null;

        public int? Select(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
        {
            Title = title;
            Options = options.ToArray();
            DefaultIndex = defaultIndex;
            return selectedIndex;
        }
    }
}
