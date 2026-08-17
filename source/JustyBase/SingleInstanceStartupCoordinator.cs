using System.Diagnostics;

namespace JustyBase;

internal sealed class SingleInstanceStartupCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public SingleInstanceStartupCoordinator(
        string mutexName,
        bool waitForVelopackRestart,
        TimeSpan? restartWaitTimeout = null,
        TimeSpan? retryInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (createdNew || !waitForVelopackRestart)
        {
            return;
        }

        TimeSpan timeout = restartWaitTimeout ?? TimeSpan.FromSeconds(15);
        TimeSpan interval = retryInterval ?? TimeSpan.FromMilliseconds(100);
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(restartWaitTimeout));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            TimeSpan remaining = timeout - stopwatch.Elapsed;
            TimeSpan waitTime = remaining < interval ? remaining : interval;

            try
            {
                if (_mutex.WaitOne(waitTime))
                {
                    _ownsMutex = true;
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous process died while owning the mutex. The current
                // process owns it after this exception and may continue startup.
                _ownsMutex = true;
                return;
            }
        }

        IsRestartWaitTimedOut = true;
    }

    public bool IsPrimary => _ownsMutex;

    public bool IsRestartWaitTimedOut { get; }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex may already have been abandoned during shutdown.
            }
        }

        _mutex.Dispose();
    }
}
