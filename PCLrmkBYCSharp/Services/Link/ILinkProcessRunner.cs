using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkProcessRunner
{
    ILinkProcessHandle Start(ProcessStartInfo startInfo);
}

public interface ILinkProcessHandle
{
    event EventHandler<LinkProcessOutputEventArgs>? OutputReceived;

    int Id { get; }

    bool HasExited { get; }

    void Stop();
}

public sealed record LinkProcessOutputEventArgs(string Line, bool IsError);
