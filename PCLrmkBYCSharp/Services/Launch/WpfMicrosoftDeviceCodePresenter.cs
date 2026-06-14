using System.Diagnostics;
using System.Windows;
using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class WpfMicrosoftDeviceCodePresenter : MicrosoftDeviceCodeStatusService
{
    private readonly IClipboardService _clipboard;

    public WpfMicrosoftDeviceCodePresenter(IClipboardService? clipboard = null)
    {
        _clipboard = clipboard ?? new ClipboardService();
    }

    public override async Task ShowAsync(MicrosoftDeviceCodeInfo info, CancellationToken cancellationToken = default)
    {
        await base.ShowAsync(info, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            var result = await dispatcher.InvokeAsync(() => ShowCore(info, cancellationToken)).Task.ConfigureAwait(false);
            UpdateStatus(BuildStatusMessage(info, result));
            return;
        }

        var localResult = ShowCore(info, cancellationToken);
        UpdateStatus(BuildStatusMessage(info, localResult));
    }

    private DeviceCodePresentationResult ShowCore(MicrosoftDeviceCodeInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var opened = TryOpenWebsite(info.VerificationUri);
        var copied = TryCopyCode(info.UserCode);
        return new DeviceCodePresentationResult(opened, copied);
    }

    private static bool TryOpenWebsite(string url)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private bool TryCopyCode(string userCode)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(userCode))
            {
                _clipboard.SetText(userCode);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string BuildStatusMessage(MicrosoftDeviceCodeInfo info, DeviceCodePresentationResult result)
    {
        if (result.OpenedVerificationPage && result.CopiedUserCode)
        {
            return "已打开微软验证网页，并复制了登录代码。请在网页中输入代码完成授权。";
        }

        if (result.OpenedVerificationPage)
        {
            return "已打开微软验证网页。请手动复制页面中的登录代码：" + info.UserCode;
        }

        if (result.CopiedUserCode)
        {
            return "已复制微软登录代码。请手动打开 " + info.VerificationUri + " 并粘贴代码完成授权。";
        }

        return "请手动打开 " + info.VerificationUri + "，输入代码 " + info.UserCode + " 完成微软授权。";
    }

    private readonly record struct DeviceCodePresentationResult(bool OpenedVerificationPage, bool CopiedUserCode);
}
