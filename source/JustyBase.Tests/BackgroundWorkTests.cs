using JustyBase.Helpers;

namespace JustyBase.Tests;

public sealed class BackgroundWorkTests
{
    [Fact]
    public async Task Begin_CountsUntilDisposed()
    {
        BackgroundWork.Forget();

        using (BackgroundWork.Begin("test"))
        {
            Assert.Equal(1, BackgroundWork.Pending);
            Assert.Contains(BackgroundWork.Held(), hold => hold.What == "test");
        }

        Assert.Equal(0, BackgroundWork.Pending);
        Assert.True(await BackgroundWork.WhenIdleAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Track_ObservesCompletionAndReachesIdle()
    {
        BackgroundWork.Forget();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        BackgroundWork.Track(gate.Task, "gate");
        Assert.Equal(1, BackgroundWork.Pending);
        Assert.False(await BackgroundWork.WhenIdleAsync(TimeSpan.FromMilliseconds(50)));

        gate.SetResult();
        Assert.True(await BackgroundWork.WhenIdleAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, BackgroundWork.Pending);
    }

    [Fact]
    public async Task Track_ObservesFaultsInsteadOfLeavingThemUnobserved()
    {
        BackgroundWork.Forget();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        BackgroundWork.Track(gate.Task, "faulting");
        gate.SetException(new InvalidOperationException("boom"));

        Assert.True(await BackgroundWork.WhenIdleAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, BackgroundWork.Pending);
    }

    [Fact]
    public void Forget_DropsHolds()
    {
        BackgroundWork.Forget();
        _ = BackgroundWork.Begin("abandoned");

        BackgroundWork.Forget();

        Assert.Equal(0, BackgroundWork.Pending);
    }
}
