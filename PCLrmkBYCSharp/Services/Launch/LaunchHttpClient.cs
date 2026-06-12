using System.Net.Http;
using System.Text;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchHttpClient : ILaunchHttpClient
{
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<string> SendAsync(LaunchHttpRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(request.Method, request.Url);
        if (!string.IsNullOrEmpty(request.Content))
        {
            message.Content = new StringContent(request.Content, Encoding.UTF8, request.ContentType);
        }

        foreach (var header in request.Headers ?? new Dictionary<string, string>())
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await _client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LaunchHttpException(response.StatusCode, response.ReasonPhrase ?? "", text, request.Url);
        }

        return text;
    }
}
