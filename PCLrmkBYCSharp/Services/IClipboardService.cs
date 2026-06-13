using System.Windows;

namespace PCLrmkBYCSharp.Services;

public interface IClipboardService
{
    void SetText(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Clipboard.SetText(text);
    }
}
