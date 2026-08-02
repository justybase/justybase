using Avalonia.Headless;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Serializes Avalonia headless sessions across test classes (one UI session at a time).
/// </summary>
internal static class HeadlessSessionGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IDisposable> AcquireAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Gate.Release();
        }
    }
}

public abstract class HeadlessSessionTestBase : IAsyncLifetime
{
    private IDisposable? _gate;
    protected HeadlessUnitTestSession? Session { get; private set; }

    public async Task InitializeAsync()
    {
        _gate = await HeadlessSessionGate.AcquireAsync().ConfigureAwait(false);
        try
        {
            Session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppSetup));
        }
        catch
        {
            _gate.Dispose();
            _gate = null;
            throw;
        }
    }

    public Task DisposeAsync()
    {
        try
        {
            Session?.Dispose();
        }
        finally
        {
            Session = null;
            _gate?.Dispose();
            _gate = null;
        }

        return Task.CompletedTask;
    }

    protected async Task RunOnUi(Action action)
    {
        Assert.NotNull(Session);
        _ = await Session!.Dispatch(() =>
        {
            action();
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
    }
}
