using JustyBase.Services.Results;

namespace JustyBase.Tests;

public sealed class ResultMemoryBudgetTests
{
    [Fact]
    public void TryReserve_RespectsLimitAndReleaseRestoresCapacity()
    {
        var budget = new ResultMemoryBudget(1_000);

        Assert.True(budget.TryReserve(600));
        Assert.False(budget.TryReserve(500));
        Assert.Equal(600, budget.UsedBytes);
        Assert.Equal(400, budget.AvailableBytes);

        budget.Release(600);

        Assert.Equal(0, budget.UsedBytes);
        Assert.True(budget.TryReserve(1_000));
    }

    [Fact]
    public void Release_DoesNotGoBelowZero()
    {
        var budget = new ResultMemoryBudget(100);

        budget.Release(50);

        Assert.Equal(0, budget.UsedBytes);
    }
}

public sealed class SpillStoreManagerTests
{
    [Fact]
    public void Constructor_RemovesOrphanedSpillFilesAndKeepsForeignFiles()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JustyBaseSpillTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string orphan = Path.Combine(directory, "spill_2000000000_orphan.db");
        string live = Path.Combine(directory, $"spill_{Environment.ProcessId}_live.db");
        string foreign = Path.Combine(directory, "keep.db");
        File.WriteAllText(orphan, "old");
        File.WriteAllText(live, "live");
        File.WriteAllText(foreign, "keep");

        try
        {
            using var manager = new SpillStoreManager(directory, quotaBytes: 1024 * 1024);

            Assert.False(File.Exists(orphan));
            Assert.True(File.Exists(live));
            Assert.True(File.Exists(foreign));
            Assert.Equal(directory, manager.Root);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateStore_TracksQuotaUsageUntilDisposed()
    {
        using var manager = new SpillStoreManager(quotaBytes: 1024 * 1024);
        var schema = new ResultSpillSchema(
            [new ResultSpillColumn("value", TypeCode.String, SpillColumnKind.Text)]);

        var store = manager.CreateStore(schema);
        store.BeginWriteBatch();
        store.WriteRow(["hello"]);
        store.EndWriteBatch();

        Assert.True(manager.UsedBytes > 0);
        Assert.True(manager.AvailableBytes < manager.QuotaBytes);

        store.Dispose();

        Assert.Equal(0, manager.UsedBytes);
    }
}
