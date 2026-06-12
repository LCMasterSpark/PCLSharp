namespace PCLrmkBYCSharp.Models;

public sealed record MicrosoftDeviceCodeInfo(
    string UserCode,
    string DeviceCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds,
    string Message);
