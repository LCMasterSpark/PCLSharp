using System.Net.Http;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Launch;

namespace PCLrmkBYCSharp.Tests;

public sealed class AppUpdateCheckServiceTests
{
    [Fact]
    public async Task AppUpdateCheckServiceReadsGitHubReleaseAndDetectsNewerVersion()
    {
        var http = new FakeLaunchHttpClient("""
        {
          "tag_name": "v0.7pre",
          "name": "Plain Craft Launcher Sharp v0.7pre",
          "html_url": "https://example.com/releases/v0.7pre",
          "published_at": "2026-06-13T10:00:00Z"
        }
        """);
        var service = new AppUpdateCheckService(http, "v0.6pre", "https://example.com/latest");

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("v0.6pre", result.CurrentVersion);
        Assert.Equal("v0.7pre", result.LatestVersion);
        Assert.Equal("https://example.com/latest", http.RequestedUrl);
        Assert.Contains("发现新版本", result.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v0.6.0", "v0.6pre", true)]
    [InlineData("v0.6pre", "v0.6.0", false)]
    [InlineData("v0.6pre", "v0.6pre", false)]
    [InlineData("v0.7pre", "v0.6pre", true)]
    [InlineData("v0.5", "v0.6pre", false)]
    public void AppUpdateCheckServiceComparesPclSharpPreVersions(string latest, string current, bool expected)
    {
        Assert.Equal(expected, AppUpdateCheckService.IsVersionNewer(latest, current));
    }

    [Fact]
    public async Task AppUpdateCheckServiceUsesExplicitSourceUrlWhenProvided()
    {
        var http = new FakeLaunchHttpClient("""{"tag_name":"v0.6pre"}""");
        var service = new AppUpdateCheckService(http, "v0.6pre", "https://example.com/default");

        await service.CheckAsync("https://example.com/custom");

        Assert.Equal("https://example.com/custom", http.RequestedUrl);
    }

    private sealed class FakeLaunchHttpClient(string response) : ILaunchHttpClient
    {
        public string RequestedUrl { get; private set; } = "";

        public Task<string> SendAsync(LaunchHttpRequest request, CancellationToken cancellationToken = default)
        {
            RequestedUrl = request.Url;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("PlainCraftLauncherSharp", request.Headers?["User-Agent"], StringComparison.Ordinal);
            return Task.FromResult(response);
        }
    }
}
