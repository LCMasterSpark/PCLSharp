using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public class MicrosoftDeviceCodeStatusService : IMicrosoftDeviceCodeStatusService
{
    private readonly object syncRoot = new();
    private MicrosoftDeviceCodeInfo? current;
    private DateTimeOffset? expiresAt;
    private string statusMessage = string.Empty;

    public event EventHandler? Changed;

    public MicrosoftDeviceCodeInfo? Current
    {
        get
        {
            lock (syncRoot)
            {
                return current;
            }
        }
    }

    public DateTimeOffset? ExpiresAt
    {
        get
        {
            lock (syncRoot)
            {
                return expiresAt;
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (syncRoot)
            {
                return current is not null && (!expiresAt.HasValue || expiresAt.Value > DateTimeOffset.UtcNow);
            }
        }
    }

    public string StatusMessage
    {
        get
        {
            lock (syncRoot)
            {
                return statusMessage;
            }
        }
    }

    public virtual Task ShowAsync(MicrosoftDeviceCodeInfo info, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            current = info;
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, info.ExpiresInSeconds));
            statusMessage = string.IsNullOrWhiteSpace(info.Message)
                ? "已打开微软验证网页，并复制了登录代码。请在网页中输入代码完成授权。"
                : info.Message;
        }

        OnChanged();
        return Task.CompletedTask;
    }

    public void UpdateStatus(string message)
    {
        lock (syncRoot)
        {
            if (current is null)
            {
                return;
            }

            statusMessage = message;
        }

        OnChanged();
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            current = null;
            expiresAt = null;
            statusMessage = string.Empty;
        }

        OnChanged();
    }

    protected void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
