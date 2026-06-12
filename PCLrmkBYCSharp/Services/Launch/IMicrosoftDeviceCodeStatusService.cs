using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IMicrosoftDeviceCodeStatusService : IMicrosoftDeviceCodePresenter
{
    event EventHandler? Changed;

    MicrosoftDeviceCodeInfo? Current { get; }

    DateTimeOffset? ExpiresAt { get; }

    bool IsActive { get; }

    string StatusMessage { get; }

    void UpdateStatus(string message);

    void Clear();
}
