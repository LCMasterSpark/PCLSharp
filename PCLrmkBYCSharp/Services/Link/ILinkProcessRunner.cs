using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkProcessRunner
{
    ILinkProcessHandle Start(ProcessStartInfo startInfo);
}

public interface ILinkProcessHandle
{
    event EventHandler<LinkProcessOutputEventArgs>? OutputReceived;

    event EventHandler<LinkProcessExitedEventArgs>? Exited;

    int Id { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    void Stop();
}

public sealed record LinkProcessOutputEventArgs(string Line, bool IsError);

public sealed record LinkProcessExitedEventArgs(int? ExitCode);
