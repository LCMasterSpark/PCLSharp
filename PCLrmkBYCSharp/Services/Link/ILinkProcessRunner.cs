using System.Diagnostics;

namespace PCLrmkBYCSharp.Services.Link;

public interface ILinkProcessRunner
{
    ILinkProcessHandle Start(ProcessStartInfo startInfo);
}

public interface ILinkProcessHandle
{
    int Id { get; }

    bool HasExited { get; }

    void Stop();
}
