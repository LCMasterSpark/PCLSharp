using System.Net;
using System.Net.Http;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Link;

namespace PCLrmkBYCSharp.Tests;

public sealed class LinkBinaryServiceTests
{
    [Fact]
    public void GetInstalledInfo_WhenFileNotFound_ReturnsEmpty()
    {
        using var temp = new TempDirectory();
        var paths = new TestAppPathService(temp.Path);
        var service = new LinkBinaryService(paths, new NullLoggerService());

        var info = service.GetInstalledInfo(LinkProviderKind.Terracotta);

        Assert.Equal("", info.Version);
        Assert.Equal(0, info.FileSize);
    }

    [Fact]
    public void GetInstalledInfo_WhenFileFound_ReturnsMetadata()
    {
        using var temp = new TempDirectory();
        var linkDir = System.IO.Path.Combine(temp.Path, "Link");
        System.IO.Directory.CreateDirectory(linkDir);
        var exePath = System.IO.Path.Combine(linkDir, "terracotta.exe");
        System.IO.File.WriteAllBytes(exePath, new byte[] { 77, 90 });

        var paths = new TestAppPathService(temp.Path);
        var service = new LinkBinaryService(paths, new NullLoggerService());

        var info = service.GetInstalledInfo(LinkProviderKind.Terracotta);

        Assert.Equal(exePath, info.ExecutablePath);
        Assert.True(info.FileSize > 0);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_WhenHttpFails_Throws()
    {
        var handler = new MockHttpMessageHandler((_) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var service = new LinkBinaryService(new TestAppPathService(""), new NullLoggerService(), http);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.FetchLatestReleaseAsync(LinkProviderKind.Terracotta));
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_ForEasyTier_ParsesRelease()
    {
        var json = JsonSerializer.Serialize(new
        {
            tag_name = "v2.1.0",
            assets = new[]
            {
                new { name = "easytier-core-x86_64-pc-windows-msvc.exe",
                      browser_download_url = "https://example.com/easytier-core.exe" }
            }
        });
        var handler = new MockHttpMessageHandler((_) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var service = new LinkBinaryService(new TestAppPathService(""), new NullLoggerService(), http);

        var release = await service.FetchLatestReleaseAsync(LinkProviderKind.EasyTier);

        Assert.Equal("2.1.0", release.Version);
        Assert.Contains("easytier-core", release.DownloadUrl.ToString());
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_ForTerracotta_SelectsWindowsAsset()
    {
        var json = JsonSerializer.Serialize(new
        {
            tag_name = "v1.5.0",
            assets = new[]
            {
                new { name = "terracotta-x86_64-pc-windows-msvc.exe",
                      browser_download_url = "https://example.com/terracotta.exe" },
                new { name = "terracotta-x86_64-unknown-linux-gnu.tar.gz",
                      browser_download_url = "https://example.com/terracotta-linux.tar.gz" }
            }
        });
        var handler = new MockHttpMessageHandler((_) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var service = new LinkBinaryService(new TestAppPathService(""), new NullLoggerService(), http);

        var release = await service.FetchLatestReleaseAsync(LinkProviderKind.Terracotta);

        Assert.Equal("1.5.0", release.Version);
        Assert.Contains("terracotta.exe", release.DownloadUrl.ToString());
        Assert.DoesNotContain("linux", release.DownloadUrl.ToString());
    }

    [Fact]
    public async Task DownloadAsync_DownloadsToLinkDirectory()
    {
        using var temp = new TempDirectory();
        var payload = new byte[] { 77, 90, 0x45, 0x52 };
        var handler = new MockHttpMessageHandler((_) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        using var http = new HttpClient(handler);
        var paths = new TestAppPathService(temp.Path);
        var service = new LinkBinaryService(paths, new NullLoggerService(), http);

        var result = await service.DownloadAsync(
            LinkProviderKind.Terracotta,
            release: new LinkBinaryReleaseInfo(
                LinkProviderKind.Terracotta, "1.0.0",
                new Uri("https://example.com/terracotta.exe"), ""));

        Assert.True(File.Exists(result));
        Assert.Equal("terracotta.exe", Path.GetFileName(result));
        var bytes = await File.ReadAllBytesAsync(result);
        Assert.Equal(payload, bytes);
    }
}

internal sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
}
