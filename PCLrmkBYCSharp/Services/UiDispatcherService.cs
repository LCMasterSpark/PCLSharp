using System.Windows.Threading;

namespace PCLrmkBYCSharp.Services;

public sealed class UiDispatcherService(Dispatcher dispatcher) : IUiDispatcherService
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Invoke(Action action)
    {
        if (CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public async Task InvokeAsync(Action action)
    {
        if (CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    public async Task<T> InvokeAsync<T>(Func<T> action)
    {
        if (CheckAccess())
        {
            return action();
        }

        return await dispatcher.InvokeAsync(action);
    }
}
