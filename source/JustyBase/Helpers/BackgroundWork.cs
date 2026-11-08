namespace JustyBase.Helpers;

/// <summary>
/// Counts background work that is still going to land on the UI thread, so tests and the
/// screenshot harness can wait until the UI has settled instead of sleeping for a guessed
/// amount of time. Every hold is named while watched (the caller's file and line when none
/// is given), which makes a hung operation easy to identify.
/// </summary>
public static class BackgroundWork
{
    private static readonly object Gate = new();
    private static readonly Dictionary<long, string> Holds = new();
    private static TaskCompletionSource _idle = CreateIdleSource();
    private static long _nextId;
    private static int _pending;

    /// <summary>The number of holds still open.</summary>
    public static int Pending
    {
        get
        {
            lock (Gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>
    /// Takes a hold until the returned scope is disposed. Disposing twice is safe.
    /// </summary>
    public static IDisposable Begin(string? what = null)
    {
        long id;
        lock (Gate)
        {
            id = ++_nextId;
            Holds[id] = string.IsNullOrWhiteSpace(what) ? "background work" : what!;
            _pending++;
        }

        return new Hold(id);
    }

    /// <summary>
    /// Holds the counter until <paramref name="task"/> completes. Any exception is observed
    /// here, so tracking a fire-and-forget task never leaves an unobserved task exception.
    /// </summary>
    public static void Track(Task task, string? what = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.IsCompleted)
        {
            Observe(task);
            return;
        }

        IDisposable hold = Begin(what);
        _ = CompleteAsync(task, hold);
    }

    /// <summary>
    /// Completes when nothing is held, or returns <see langword="false"/> when the timeout
    /// elapses first.
    /// </summary>
    public static async Task<bool> WhenIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero)
        {
            timeout = TimeSpan.Zero;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        while (true)
        {
            Task idle;
            lock (Gate)
            {
                if (_pending == 0)
                {
                    return true;
                }

                idle = _idle.Task;
            }

            Task delay = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);
            Task completed = await Task.WhenAny(idle, delay).ConfigureAwait(false);
            if (completed != idle)
            {
                // Timed out or cancelled: report the last exact state.
                return Pending == 0;
            }
        }
    }

    /// <summary>The holds now open, oldest first, with the name they were taken under.</summary>
    public static IReadOnlyList<(string What, long Id)> Held()
    {
        lock (Gate)
        {
            return Holds.OrderBy(pair => pair.Key)
                .Select(pair => (pair.Value, pair.Key))
                .ToArray();
        }
    }

    /// <summary>
    /// Drops every hold. Used by a test session that is tearing down its UI thread: nothing
    /// will ever dispose those scopes, and leaving them would make the next session wait.
    /// </summary>
    public static void Forget()
    {
        TaskCompletionSource? toComplete = null;
        lock (Gate)
        {
            Holds.Clear();
            _pending = 0;
            toComplete = _idle;
            _idle = CreateIdleSource();
        }

        toComplete.TrySetResult();
    }

    private static async Task CompleteAsync(Task task, IDisposable hold)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a normal way for tracked work to end.
        }
        catch (Exception) when (task.IsFaulted)
        {
            // Observed: tracking exists to wait, not to rethrow.
            _ = task.Exception;
        }
        finally
        {
            hold.Dispose();
        }
    }

    private static void Observe(Task task)
    {
        if (!task.IsFaulted && !task.IsCanceled)
        {
            return;
        }

        _ = task.Exception;
    }

    private static TaskCompletionSource CreateIdleSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void End(long id)
    {
        TaskCompletionSource? toComplete = null;
        lock (Gate)
        {
            if (!Holds.Remove(id))
            {
                return;
            }

            _pending--;
            if (_pending == 0)
            {
                toComplete = _idle;
                _idle = CreateIdleSource();
            }
        }

        toComplete?.TrySetResult();
    }

    private sealed class Hold(long id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                End(id);
            }
        }
    }
}
