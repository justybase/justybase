using System.Data;
using JustyBase.Helpers;
using JustyBase.Services.Results;

namespace JustyBase.Tests;

public sealed class SpillResultReaderTests
{
    private static ResultSpillStore CreateStore(SpillStoreManager manager)
    {
        var schema = new ResultSpillSchema(
        [
            new ResultSpillColumn("n", TypeCode.Int32, SpillColumnKind.Integral),
            new ResultSpillColumn("v", TypeCode.String, SpillColumnKind.Text)
        ]);

        ResultSpillStore store = manager.CreateStore(schema);
        store.PageSize = 2;
        store.BeginWriteBatch();
        for (int i = 0; i < 6; i++)
        {
            store.WriteRow([i, $"v{i}"]);
        }

        store.EndWriteBatch();
        return store;
    }

    [Fact]
    public void Read_StreamsEveryFilteredRowInSortOrder()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager);
        var filter = new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.GreaterThanOrEqual, 2)]);
        int filtered = store.SetFilter(filter);
        Assert.Equal(4, filtered);

        using var reader = new SpillResultReader(store, [new SpillSortKey(0, Descending: true)], filter);

        Assert.True(reader.HasRows);
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("n", reader.GetName(0));
        Assert.Equal("v", reader.GetName(1));
        Assert.Equal(1, reader.GetOrdinal("v"));
        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal("Int32", reader.GetDataTypeName(0));

        var values = new List<int>();
        while (reader.Read())
        {
            int n = reader.GetInt32(0);
            values.Add(n);
            Assert.Equal($"v{n}", reader.GetString(1));
        }

        Assert.Equal([5, 4, 3, 2], values);
        Assert.False(reader.Read());

        reader.Dispose();
        Assert.True(reader.IsClosed);
    }

    [Fact]
    public void Read_WithNoMatchingRows_ReturnsFalse()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager);
        var filter = new SpillFilter(
            SearchText: "does-not-match",
            ContainsSearch: true,
            []);
        store.SetFilter(filter);

        using var reader = new SpillResultReader(store, sortKeys: null, filter);

        Assert.False(reader.HasRows);
        Assert.False(reader.Read());
    }

    [Fact]
    public void GetValues_ReturnsDBNullForMissingValues()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = manager.CreateStore(new ResultSpillSchema(
            [new ResultSpillColumn("v", TypeCode.String, SpillColumnKind.Text)]));
        store.BeginWriteBatch();
        store.WriteRow([null]);
        store.EndWriteBatch();

        using var reader = new SpillResultReader(store, sortKeys: null);
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));

        var values = new object[1];
        Assert.Equal(1, reader.GetValues(values));
        Assert.Same(DBNull.Value, values[0]);
    }
}
