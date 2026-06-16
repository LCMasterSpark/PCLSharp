using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using PCLrmkBYCSharp.Models;
using PCLrmkBYCSharp.Services;
using PCLrmkBYCSharp.Services.Downloads;

namespace PCLrmkBYCSharp.Tests;

public sealed class FabricLoaderInstallServiceTests
{
    [Fact]
    public void BuildProfileUrl_ReturnsCorrectUrl()
    {
        var url = FabricLoaderInstallService.BuildProfileUrl("1.20.1", "0.15.11");
        Assert.Contains("meta.fabricmc.net", url);
        Assert.Contains("1.20.1", url);
        Assert.Contains("0.15.11", url);
    }

    [Theory]
    [InlineData("", "0.15.11")]
    [InlineData("1.20.1", "")]
    public async Task CreateInstallPlanAsync_ThrowsOnMissingVersion(string mcVer, string loaderVer)
    {
        var svc = new FabricLoaderInstallService(new RealByteClient(), new PassthroughSource(), new NullLoggerService());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateInstallPlanAsync("", "t", "", mcVer, loaderVer));
    }

    [Fact]
    public async Task CreateInstallPlanAsync_WritesVersionJson()
    {
        var p = JsonSerializer.Serialize(new { libraries = new object[] { } });
        var h = new FH(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(p, System.Text.Encoding.UTF8, "application/json")
        });
        using var t = new TempDirectory();
        var s = new FabricLoaderInstallService(new FB(h), new PassthroughSource(), new NullLoggerService());
        await s.CreateInstallPlanAsync(t.Path, "Fab", Path.Combine(t.Path, "v", "Fab"), "1.20.1", "0.15.11");
        var j = Path.Combine(t.Path, "v", "Fab", "Fab.json");
        Assert.True(File.Exists(j));
        using var d = JsonDocument.Parse(await File.ReadAllTextAsync(j));
        Assert.Equal("Fab", d.RootElement.GetProperty("id").GetString());
    }
}

internal sealed class FH(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(f(r)); }
internal sealed class FB(HttpMessageHandler h) : IDownloadByteClient
{ readonly HttpClient c = new(h); public async Task<byte[]> GetBytesAsync(string u, bool b, CancellationToken ct) => await c.GetAsync(u, ct).ContinueWith(x => x.Result.Content.ReadAsByteArrayAsync()).Unwrap(); public Task<long> DownloadToFileAsync(string u, string l, bool b, IProgress<long>? p, CancellationToken ct) => throw new NotImplementedException(); }
internal sealed class PassthroughSource : IDownloadSourceService
{ public bool PreferOfficialDownloadsWhenAuto => true; public IReadOnlyList<string> OrderSources(IEnumerable<string> o, IEnumerable<string> m) => o.ToList(); public IReadOnlyList<string> GetLibrarySources(string o) => new[] { o }; public IReadOnlyList<string> GetAssetSources(string o) => new[] { o }; public string GetModMirrorSource(string o) => o; public IReadOnlyList<string> GetModFileSources(string o) => new[] { o }; public IReadOnlyList<string> GetLauncherOrMetaSources(string o) => new[] { o }; public void ReportOfficialVersionListLatency(TimeSpan e) { } }
internal sealed class RealByteClient : IDownloadByteClient
{ public Task<byte[]> GetBytesAsync(string u, bool b, CancellationToken ct) => throw new NotImplementedException(); public Task<long> DownloadToFileAsync(string u, string l, bool b, IProgress<long>? p, CancellationToken ct) => throw new NotImplementedException(); }
