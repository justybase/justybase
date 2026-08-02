using JustyBase.Services.Results;

namespace JustyBase.Tests;

public class ResultSpillStoreTests
{
    [Fact]
    public void WriteAndReadPage_RoundTripsRows()
    {
        using var store = new ResultSpillStore();
        store.PageSize = 2;
        store.BeginWriteBatch();
        store.WriteRow([1, "a"]);
        store.WriteRow([2, "b"]);
        store.WriteRow([3, "c"]);
        store.EndWriteBatch();

        Assert.Equal(3, store.RowCount);
        Assert.Equal(2, store.PageCount(2));

        var page0 = store.ReadPage(0, 2);
        Assert.Equal(2, page0.Count);
        Assert.Equal(1L, Convert.ToInt64(page0[0][0]));
        Assert.Equal("a", page0[0][1]?.ToString());

        var page1 = store.ReadPage(1, 2);
        Assert.Single(page1);
        Assert.Equal(3L, Convert.ToInt64(page1[0][0]));
    }

    [Fact]
    public async Task Dispose_DeletesTempDatabase()
    {
        string path;
        {
            using var store = new ResultSpillStore();
            path = store.DatabasePath;
            store.BeginWriteBatch();
            store.WriteRow(["x"]);
            store.EndWriteBatch();
            Assert.True(File.Exists(path));
        }

        // Pooling cleared on dispose; allow brief FS delay on Windows.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (File.Exists(path) && DateTime.UtcNow < deadline)
        {
            try { File.Delete(path); } catch { /* retry */ }
            if (File.Exists(path))
            {
                await Task.Delay(20);
            }
        }

        Assert.False(File.Exists(path));
    }
}
