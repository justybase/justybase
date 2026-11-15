namespace JustyBase.Helpers;

/// <summary>
/// Minimal async mutex built from a task chain. Unlike <see cref="SemaphoreSlim"/>
/// it owns no disposable OS handle, so a view model can hold one without becoming
/// <see cref="IDisposable"/> merely for the lock.
/// </summary>
internal sealed class AsyncMutex
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;

    public async Task<IDisposable> LockAsync()
    {
        TaskCompletionSource release;
        Task previous;

        lock (_gate)
        {
            previous = _tail;
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _tail = release.Task;
        }

        try
        {
            await previous.ConfigureAwait(true);
        }
        catch
        {
            release.TrySetResult();
            throw;
        }

        return new Releaser(release);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly TaskCompletionSource _release;
        private int _disposed;

        public Releaser(TaskCompletionSource release)
        {
            _release = release;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _release.TrySetResult();
            }
        }
    }
}
