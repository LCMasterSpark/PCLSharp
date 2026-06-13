using System.Diagnostics;
using System.IO;
using System.Text;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkProcessRunner : ILinkProcessRunner
{
    public ILinkProcessHandle Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("启动联机后端进程失败。");
        return new LinkProcessHandle(process);
    }

    public static ProcessStartInfo CreateStartInfo(string executablePath, string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
        };
    }

    private sealed class LinkProcessHandle(Process process) : ILinkProcessHandle
    {
        public int Id => process.Id;

        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public void Stop()
        {
            if (!HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }
    }
}
