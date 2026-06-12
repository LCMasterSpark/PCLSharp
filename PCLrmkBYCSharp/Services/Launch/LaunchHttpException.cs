using System.Net;
using System.Net.Http;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed class LaunchHttpException(
    HttpStatusCode statusCode,
    string reasonPhrase,
    string responseBody,
    string url)
    : HttpRequestException($"HTTP {(int)statusCode} {reasonPhrase}: {responseBody}")
{
    public HttpStatusCode StatusCodeValue { get; } = statusCode;

    public string ReasonPhrase { get; } = reasonPhrase;

    public string ResponseBody { get; } = responseBody;

    public string Url { get; } = url;
}
