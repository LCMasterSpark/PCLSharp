using System.Diagnostics;
using System.Text;
using System.IO;

namespace PCLrmkBYCSharp.Services;

public sealed class AppLoggerService(IAppPathService paths) : IAppLoggerService
{
    private readonly object _fileLock = new();
    private string? _logFilePath;

    public void Initialize()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(5));
        paths.EnsureCreated();
        _logFilePath = Path.Combine(paths.LogsDirectory, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        WriteLine("日志服务已初始化", null);
    }

    public void Info(string message)
    {
        WriteLine(message, null);
    }

    public void Warn(string message)
    {
        WriteLine(message, null);
    }

    public void Error(Exception exception, string message)
    {
        WriteLine(message, exception);
    }

    private void WriteLine(string message, Exception? exception)
    {
        var line = $"{DateTime.Now:O} [{(exception is not null ? "Error" : "Info")}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        Debug.WriteLine(line);
        if (_logFilePath is null)
        {
            return;
        }

        lock (_fileLock)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
