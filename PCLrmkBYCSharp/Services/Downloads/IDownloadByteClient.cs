using System.IO;

namespace PCLrmkBYCSharp.Services.Downloads;

public interface IDownloadByteClient
{
    Task<byte[]> GetBytesAsync(string url, bool simulateBrowserHeaders = false, CancellationToken cancellationToken = default);

    async Task<long> DownloadToFileAsync(string url, string localPath, bool simulateBrowserHeaders = false, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var bytes = await GetBytesAsync(url, simulateBrowserHeaders, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(localPath, bytes, cancellationToken).ConfigureAwait(false);
        progress?.Report(bytes.Length);
        return bytes.Length;
    }
}
