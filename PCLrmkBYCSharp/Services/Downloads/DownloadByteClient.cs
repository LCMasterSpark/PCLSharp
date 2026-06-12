using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PCLrmkBYCSharp.Services.Downloads;

public sealed class DownloadByteClient : IDownloadByteClient
{
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public async Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, url, simulateBrowserHeaders);

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> DownloadToFileAsync(string url, string localPath, bool simulateBrowserHeaders = false, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var existingLength = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, url, simulateBrowserHeaders);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var canAppend = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var fileMode = canAppend ? FileMode.Append : FileMode.Create;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(localPath, fileMode, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long downloaded = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;
            progress?.Report(downloaded);
        }

        return downloaded;
    }

    private static void ApplyHeaders(HttpRequestMessage request, string url, bool simulateBrowserHeaders)
    {
        if (simulateBrowserHeaders)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 PlainCraftLauncherSharp");
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.Equals("api.curseforge.com", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            }
        }
    }
}
