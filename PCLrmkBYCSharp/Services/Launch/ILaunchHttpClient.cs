namespace PCLrmkBYCSharp.Services.Launch;

public interface ILaunchHttpClient
{
    Task<string> SendAsync(LaunchHttpRequest request, CancellationToken cancellationToken = default);
}
