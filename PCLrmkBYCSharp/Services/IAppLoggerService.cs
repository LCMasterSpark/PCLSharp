namespace PCLrmkBYCSharp.Services;

public interface IAppLoggerService
{
    void Initialize();

    void Info(string message);

    void Warn(string message);

    void Error(Exception exception, string message);
}
