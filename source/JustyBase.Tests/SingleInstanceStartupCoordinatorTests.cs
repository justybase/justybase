namespace JustyBase.Tests;

public sealed class SingleInstanceStartupCoordinatorTests
{
    [Fact]
    public async Task RestartWaitsUntilExistingInstanceReleasesMutex()
    {
        string mutexName = $"Local\\JustyBase_Test_{Guid.NewGuid():N}";
        using Mutex existingInstance = new(initiallyOwned: true, mutexName, out bool createdNew);
        Assert.True(createdNew);

        Task<SingleInstanceStartupCoordinator> startup = Task.Run(() =>
            new SingleInstanceStartupCoordinator(
                mutexName,
                waitForVelopackRestart: true,
                restartWaitTimeout: TimeSpan.FromSeconds(2),
                retryInterval: TimeSpan.FromMilliseconds(10)));

        Thread.Sleep(250);
        existingInstance.ReleaseMutex();

        using SingleInstanceStartupCoordinator coordinator = await startup;
        Assert.True(coordinator.IsPrimary);
        Assert.False(coordinator.IsRestartWaitTimedOut);
    }

    [Fact]
    public void NormalSecondInstanceDoesNotWaitForExistingInstance()
    {
        string mutexName = $"Local\\JustyBase_Test_{Guid.NewGuid():N}";
        using Mutex existingInstance = new(initiallyOwned: true, mutexName, out bool createdNew);
        Assert.True(createdNew);

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
        using Mutex existingInstance = new(initiallyOwned: true, mutexName, out bool createdNew);
        Assert.True(createdNew);

        using SingleInstanceStartupCoordinator coordinator = await Task.Run(() =>
            new SingleInstanceStartupCoordinator(
                mutexName,
                waitForVelopackRestart: true,
                restartWaitTimeout: TimeSpan.FromMilliseconds(100),
                retryInterval: TimeSpan.FromMilliseconds(10)));

        Assert.False(coordinator.IsPrimary);
        Assert.True(coordinator.IsRestartWaitTimedOut);
    }
}
