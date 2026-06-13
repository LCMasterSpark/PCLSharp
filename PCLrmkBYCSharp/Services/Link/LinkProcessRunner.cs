using System.Diagnostics;
using System.IO;
using System.Text;

namespace PCLrmkBYCSharp.Services.Link;

public sealed class LinkProcessRunner : ILinkProcessRunner
{
    public ILinkProcessHandle Start(ProcessStartInfo startInfo)
    {
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        var handle = new LinkProcessHandle(process);
        process.OutputDataReceived += (_, args) => handle.PublishOutput(args.Data, isError: false);
        process.ErrorDataReceived += (_, args) => handle.PublishOutput(args.Data, isError: true);
        process.Exited += (_, _) => handle.PublishExited();

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("启动联机后端进程失败。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return handle;
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
        public event EventHandler<LinkProcessOutputEventArgs>? OutputReceived;

        public event EventHandler<LinkProcessExitedEventArgs>? Exited;

        public int Id => process.Id;

        public int? ExitCode
        {
            get
            {
                try
                {
                    return process.HasExited ? process.ExitCode : null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

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

        public void PublishOutput(string? line, bool isError)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                OutputReceived?.Invoke(this, new LinkProcessOutputEventArgs(line, isError));
            }
        }

        public void PublishExited()
        {
            Exited?.Invoke(this, new LinkProcessExitedEventArgs(ExitCode));
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
