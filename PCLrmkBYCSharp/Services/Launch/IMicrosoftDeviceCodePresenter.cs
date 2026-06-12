using PCLrmkBYCSharp.Models;

namespace PCLrmkBYCSharp.Services.Launch;

public interface IMicrosoftDeviceCodePresenter
{
    Task ShowAsync(MicrosoftDeviceCodeInfo info, CancellationToken cancellationToken = default);
}
