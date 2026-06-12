namespace PCLrmkBYCSharp.Services;

public interface IUiDispatcherService
{
    bool CheckAccess();

    void Invoke(Action action);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> action);
}
