using System.Net.Http;

namespace PCLrmkBYCSharp.Services.Launch;

public sealed record LaunchHttpRequest(
    string Url,
    HttpMethod Method,
    string Content = "",
    string ContentType = "application/json",
    IReadOnlyDictionary<string, string>? Headers = null);
