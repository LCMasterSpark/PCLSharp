using System.Diagnostics;
using System.IO;
using MeloongCore;

namespace PCLrmkBYCSharp.Services;

public sealed class AppLoggerService(IAppPathService paths) : IAppLoggerService
{
    private readonly object _fileLock = new();
    private string? _logFilePath;

    public void Initialize()
    {
        paths.EnsureCreated();
        _logFilePath = Path.Combine(paths.LogsDirectory, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        MeloongCore.Main.Init(new BaseLogger { MinLevel = LogLevel.Trace });
        WriteLine(LogLevel.Info, "日志服务已初始化", null);
    }

    public void Info(string message)
    {
        Logger.Info(message, LogBehavior.None);
        WriteLine(LogLevel.Info, message, null);
    }

    public void Warn(string message)
    {
        Logger.Warn(message, LogBehavior.None);
        WriteLine(LogLevel.Warn, message, null);
    }

    public void Error(Exception exception, string message)
    {
        Logger.Error(exception, message, LogBehavior.None);
        WriteLine(LogLevel.Error, message, exception);
    }

    private void WriteLine(LogLevel level, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:O} [{level}] {message}";
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
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }
}
