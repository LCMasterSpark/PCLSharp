using System.Diagnostics;

namespace PCLrmkBYCSharp.Services;

public sealed class ExternalUrlService : IExternalUrlService
{
    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("链接不能为空。", nameof(url));
        }

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
