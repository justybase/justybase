using System.Data;
using JustyBase.Services.Results;
using Microsoft.Data.Sqlite;

namespace JustyBase.Tests;

public sealed class ResultSpillStoreTests
{
    private static ResultSpillColumn Column(TypeCode typeCode, string? typeName = null)
        => new($"c{typeCode}", typeCode, ResultSpillSchema.KindFor(typeCode, typeName));

    private static ResultSpillStore CreateStore(SpillStoreManager manager, params ResultSpillColumn[] columns)
        => manager.CreateStore(new ResultSpillSchema(columns));

    private static ResultSpillStore CreateStore(SpillStoreManager manager, params TypeCode[] typeCodes)
        => CreateStore(manager, typeCodes.Select(code => Column(code)).ToArray());

    [Fact]
    public void WriteAndReadPage_RoundTripsRows()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager, TypeCode.Int32, TypeCode.String);
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
        Assert.IsType<int>(page0[0][0]);
        Assert.Equal(1, page0[0][0]);
        Assert.Equal("a", page0[0][1]);

        var page1 = store.ReadPage(1, 2);
        Assert.Single(page1);
        Assert.IsType<int>(page1[0][0]);
        Assert.Equal(3, page1[0][0]);
    }

    [Fact]
    public void WriteAndReadPage_PreservesExactScalarTypes()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(
            manager,
            Column(TypeCode.Decimal),
            Column(TypeCode.DateTime),
            Column(TypeCode.Boolean),
            Column(TypeCode.Object, "bytea"));
        var timestamp = new DateTime(2026, 11, 15, 8, 5, 13, 123, DateTimeKind.Unspecified).AddTicks(4567);

        store.BeginWriteBatch();
        store.WriteRow([12345678901234567890.123456789m, timestamp, true, new byte[] { 1, 2, 3 }]);
        store.EndWriteBatch();

        object?[] row = Assert.Single(store.ReadPage(0, 10));
        Assert.Equal(12345678901234567890.123456789m, row[0]);
        Assert.Equal(timestamp, row[1]);
        Assert.Equal(true, row[2]);
        Assert.Equal(new byte[] { 1, 2, 3 }, row[3]);
    }

    [Fact]
    public void Storage_UsesTypedColumnsInsteadOfOneJsonPayload()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(
            manager,
            Column(TypeCode.Int32),
            Column(TypeCode.String),
            Column(TypeCode.Decimal),
            Column(TypeCode.Object, "bytea"));

        store.BeginWriteBatch();
        store.WriteRow([7, "abc", 12.50m, new byte[] { 9 }]);
        store.EndWriteBatch();

        using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(c0), typeof(c1), typeof(c2), typeof(c3) FROM rows LIMIT 1;";
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("integer", reader.GetString(0));
        Assert.Equal("text", reader.GetString(1));
        Assert.Equal("text", reader.GetString(2));
        Assert.Equal("blob", reader.GetString(3));
    }

    [Fact]
    public void ReadPage_AppliesTypedOrdering()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager, TypeCode.Int32, TypeCode.Decimal);
        store.BeginWriteBatch();
        store.WriteRow([3, 30.5m]);
        store.WriteRow([1, 10.25m]);
        store.WriteRow([2, 20.75m]);
        store.EndWriteBatch();

        var ascending = store.ReadPage(0, 10, [new SpillSortKey(0, Descending: false)]);
        Assert.Equal([1, 2, 3], ascending.Select(row => row[0]));

        var descending = store.ReadPage(0, 10, [new SpillSortKey(1, Descending: true)]);
        Assert.Equal([30.5m, 20.75m, 10.25m], descending.Select(row => row[1]));
    }

    [Fact]
    public void SetFilter_MatchesContainsAndExactText()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager, TypeCode.Int32, TypeCode.String);
        store.BeginWriteBatch();
        store.WriteRow([1, "Alpha"]);
        store.WriteRow([2, "alphabet"]);
        store.WriteRow([3, "beta"]);
        store.EndWriteBatch();

        int contains = store.SetFilter(new SpillFilter("alpha", ContainsSearch: true, []));
        Assert.Equal(2, contains);
        Assert.Equal(2, store.FilteredRowCount);

        int exact = store.SetFilter(new SpillFilter("ALPHA", ContainsSearch: false, []));
        Assert.Equal(1, exact);
        var row = Assert.Single(store.ReadPage(0, 10));
        Assert.Equal(1, row[0]);

        int all = store.SetFilter(SpillFilter.Empty);
        Assert.Equal(3, all);
    }

    [Fact]
    public void SetFilter_AppliesColumnPredicates()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager, TypeCode.Int32, TypeCode.String);
        store.BeginWriteBatch();
        store.WriteRow([1, "a"]);
        store.WriteRow([2, "b"]);
        store.WriteRow([3, "c"]);
        store.EndWriteBatch();

        int matching = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.GreaterThanOrEqual, 2)]));

        Assert.Equal(2, matching);
        Assert.Equal([2, 3], store.ReadPage(0, 10).Select(row => row[0]));

        int inList = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(1, SpillFilterOperator.In, Values: ["a", "c"])]));

        Assert.Equal(2, inList);
    }

    [Fact]
    public void SetFilter_HandlesNullAndTextSuffixPredicates()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = CreateStore(manager, TypeCode.String);
        store.BeginWriteBatch();
        store.WriteRow(["alpha"]);
        store.WriteRow(["beta"]);
        store.WriteRow([null]);
        store.EndWriteBatch();

        int suffix = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.EndsWith, "ta")]));
        Assert.Equal(1, suffix);
        Assert.Equal("beta", Assert.IsType<string>(Assert.Single(store.ReadPage(0, 10))[0]));

        int withNull = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.In, Values: [null, "alpha"], IncludeNull: true)]));
        Assert.Equal(2, withNull);

        int withoutNull = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.In, Values: [null, "alpha"], IncludeNull: false)]));
        Assert.Equal(1, withoutNull);

        int onlyNull = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.In, Values: [null], IncludeNull: true)]));
        Assert.Equal(1, onlyNull);

        int equalsNull = store.SetFilter(new SpillFilter(
            SearchText: null,
            ContainsSearch: true,
            [new SpillColumnFilter(0, SpillFilterOperator.Equal)]));
        Assert.Equal(1, equalsNull);
    }

    [Fact]
    public void QuotaExceeded_KeepsAlreadyWrittenRows()
    {
        using var manager = new SpillStoreManager(quotaBytes: 300);
        using ResultSpillStore store = CreateStore(manager, TypeCode.String);

        store.BeginWriteBatch();
        Assert.Throws<SpillQuotaExceededException>(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                store.WriteRow([new string('x', 64)]);
            }
        });
        store.EndWriteBatch();

        Assert.InRange(store.RowCount, 1, 9);
        Assert.True(manager.UsedBytes > 0);
        Assert.NotEmpty(store.ReadPage(0, 10));
    }

    [Fact]
    public async Task Dispose_DeletesTempDatabase()
    {
        string path;
        {
            using var manager = new SpillStoreManager();
            using ResultSpillStore store = CreateStore(manager, TypeCode.String);
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
