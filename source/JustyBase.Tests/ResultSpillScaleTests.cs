using System.Data;
using JustyBase.Helpers;
using JustyBase.Services.Results;

namespace JustyBase.Tests;

/// <summary>
/// Regression coverage for the result-store path at a size where spilling is
/// expected. Keeps the paging/filter/sort/streaming contract honest without
/// depending on a live database or a wall-clock budget.
/// </summary>
public sealed class ResultSpillScaleTests
{
    private const int RowCount = 50_000;

    private static ResultSpillStore CreateStore(SpillStoreManager manager)
    {
        var schema = new ResultSpillSchema(
        [
            new ResultSpillColumn("id", TypeCode.Int32, SpillColumnKind.Integral),
            new ResultSpillColumn("name", TypeCode.String, SpillColumnKind.Text)
        ]);

        ResultSpillStore store = manager.CreateStore(schema);
        store.PageSize = 5_000;
        store.BeginWriteBatch();
        for (int i = 0; i < RowCount; i++)
        {
            store.WriteRow([i, $"row-{i}"]);
        }

        store.EndWriteBatch();
        return store;
    }

    [Fact]
    public void LargeResult_PagesFiltersSortsAndStreamsEveryRow()
    {
        using var manager = new SpillStoreManager(quotaBytes: 256L * 1024 * 1024);
        using ResultSpillStore store = CreateStore(manager);

        Assert.Equal(RowCount, store.RowCount);
        Assert.Equal(RowCount / 5_000, store.PageCount(5_000));

        var lastPage = store.ReadPage(store.PageCount(5_000) - 1, 5_000);
        Assert.NotEmpty(lastPage);
        Assert.Equal(RowCount - 1, lastPage[^1][0]);

        var filter = new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.GreaterThanOrEqual, RowCount / 2)]);
        int filtered = store.SetFilter(filter);
        Assert.Equal(RowCount / 2, filtered);

        var firstSorted = store.ReadPage(0, 10, [new SpillSortKey(0, Descending: true)]);
        Assert.Equal(RowCount - 1, firstSorted[0][0]);
        Assert.Equal(RowCount - 2, firstSorted[1][0]);

        using var reader = new SpillResultReader(store, [new SpillSortKey(0, Descending: false)], filter);
        int read = 0;
        int previous = -1;
        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            Assert.True(id > previous);
            previous = id;
            read++;
        }

        Assert.Equal(RowCount / 2, read);
    }

    [Fact]
    public async Task LargeResult_BuildsClipboardTextFromEveryFilteredRow()
    {
        using var manager = new SpillStoreManager(quotaBytes: 256L * 1024 * 1024);
        using ResultSpillStore store = CreateStore(manager);
        var filter = new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.LessThan, 10)]);
        store.SetFilter(filter);

        using var reader = new SpillResultReader(store, sortKeys: null, filter);
        var service = new JustyBase.Services.DataGrid.DataGridClipboardService();
        string text = await service.BuildAllRowsTextAsync(["id", "name"], reader);

        Assert.Contains("id\tname", text);
        Assert.Contains("9\trow-9", text);
        Assert.DoesNotContain("10\trow-10", text);

        int lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(11, lines);
    }
}
