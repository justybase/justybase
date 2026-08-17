namespace JustyBase.Tests;

public sealed class SingleInstanceStartupCoordinatorTests
{
    [Fact]
    public async Task RestartWaitsUntilExistingInstanceReleasesMutex()
    {
        string mutexName = $"Local\\JustyBase_Test_{Guid.NewGuid():N}";
        using DedicatedMutexHolder existingInstance = new(mutexName);

        Task<SingleInstanceStartupCoordinator> startup = Task.Run(() =>
            new SingleInstanceStartupCoordinator(
                mutexName,
                waitForVelopackRestart: true,
                restartWaitTimeout: TimeSpan.FromSeconds(2),
                retryInterval: TimeSpan.FromMilliseconds(10)));

        Thread.Sleep(250);
        existingInstance.Release();

        using SingleInstanceStartupCoordinator coordinator = await startup;
        Assert.True(coordinator.IsPrimary);
        Assert.False(coordinator.IsRestartWaitTimedOut);
    }

    [Fact]
    public void NormalSecondInstanceDoesNotWaitForExistingInstance()
    {
        string mutexName = $"Local\\JustyBase_Test_{Guid.NewGuid():N}";
        using DedicatedMutexHolder existingInstance = new(mutexName);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using SingleInstanceStartupCoordinator coordinator = new(mutexName, waitForVelopackRestart: false);

        Assert.False(coordinator.IsPrimary);
        Assert.False(coordinator.IsRestartWaitTimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RestartWaitTimesOutWhenExistingInstanceDoesNotReleaseMutex()
    {
        string mutexName = $"Local\\JustyBase_Test_{Guid.NewGuid():N}";
        using DedicatedMutexHolder existingInstance = new(mutexName);

        using SingleInstanceStartupCoordinator coordinator = await Task.Run(() =>
            new SingleInstanceStartupCoordinator(
                mutexName,
                waitForVelopackRestart: true,
                restartWaitTimeout: TimeSpan.FromMilliseconds(100),
                retryInterval: TimeSpan.FromMilliseconds(10)));

        Assert.False(coordinator.IsPrimary);
        Assert.True(coordinator.IsRestartWaitTimedOut);
    }

    private sealed class DedicatedMutexHolder : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private readonly string _mutexName;

        public DedicatedMutexHolder(string mutexName)
        {
            _mutexName = mutexName;
            _thread = new Thread(OwnMutex)
            {
                IsBackground = true
            };
            _thread.Start();
            _ready.Wait();
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _thread.Join();
            _ready.Dispose();
            _release.Dispose();
        }

        private void OwnMutex()
        {
            using Mutex mutex = new(initiallyOwned: true, _mutexName, out bool createdNew);
            if (!createdNew)
            {
                throw new InvalidOperationException("Test mutex was already created.");
            }

            _ready.Set();
            _release.Wait();
            mutex.ReleaseMutex();
        }
    }
}
