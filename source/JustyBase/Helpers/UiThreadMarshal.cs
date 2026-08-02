using Avalonia.Threading;

namespace JustyBase.Helpers;

/// <summary>
/// Non-blocking UI marshaling helpers. Prefer <see cref="InvokeAsync{T}"/> over
/// blocking <c>InvokeAsync(...).GetAwaiter().GetResult()</c> to avoid UI deadlocks.
/// </summary>
internal static class UiThreadMarshal
{
    public static Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }

    public static Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return Dispatcher.UIThread.InvokeAsync(func, priority).GetTask();
    }

    public static Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    public static Task InvokeAsync(Action action, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action, priority).GetTask();
    }
}
