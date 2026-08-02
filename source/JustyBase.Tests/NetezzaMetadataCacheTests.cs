using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class NetezzaMetadataCacheTests
{
    [Fact]
    public void TryGetTable_IsCaseInsensitive()
    {
        var cache = new NetezzaMetadataCache();
        cache.MergeTable(
            new TableInfo("Emp", "Public", "Db1", Columns: [new ColumnInfo("ID")]),
            TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGetTable("db1", "public", "emp", out var table));
        Assert.Equal("Emp", table.Name);
        Assert.Single(table.Columns!);
    }

    [Fact]
    public void MergeTable_EmptyIncomingColumns_PreservesExisting()
    {
        var cache = new NetezzaMetadataCache();
        cache.MergeTable(
            new TableInfo("T", "S", "D", Columns: [new ColumnInfo("A"), new ColumnInfo("B")]),
            TimeSpan.FromMinutes(5));
        cache.MergeTable(
            new TableInfo("T", "S", "D", Columns: []),
            TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGetTable("D", "S", "T", out var table));
        Assert.Equal(2, table.Columns!.Count);
    }

    [Fact]
    public void MergeTable_IsView_IsOrCombined()
    {
        var cache = new NetezzaMetadataCache();
        cache.MergeTable(new TableInfo("V", "S", "D", IsView: false), TimeSpan.FromMinutes(5));
        cache.MergeTable(new TableInfo("V", "S", "D", IsView: true), TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGetTable("D", "S", "V", out var table));
        Assert.True(table.IsView);
    }

    [Fact]
    public void GetFreshTables_SkipsExpiredEntries()
    {
        var cache = new NetezzaMetadataCache();
        cache.MergeTable(new TableInfo("Fresh", "S", "D"), TimeSpan.FromMinutes(5));
        cache.MergeTable(new TableInfo("Stale", "S", "D"), TimeSpan.FromMilliseconds(1));
        WaitUntil(() => !cache.TryGetTable("D", "S", "Stale", out _), TimeSpan.FromSeconds(2));

        var fresh = cache.GetFreshTables();

        Assert.Contains(fresh, t => t.Name == "Fresh");
        Assert.DoesNotContain(fresh, t => t.Name == "Stale");
        Assert.False(cache.TryGetTable("D", "S", "Stale", out _));
    }

    [Fact]
    public void BumpGeneration_IncrementsGeneration()
    {
        var cache = new NetezzaMetadataCache();
        var before = cache.Generation;
        cache.BumpGeneration();
        Assert.Equal(before + 1, cache.Generation);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new NetezzaMetadataCache();
        cache.MergeTable(new TableInfo("T", "S", "D"), TimeSpan.FromMinutes(5));
        cache.Clear();

        Assert.Empty(cache.GetFreshTables());
        Assert.False(cache.TryGetTable("D", "S", "T", out _));
    }

    [Fact]
    public void EnsureColumns_EmptyColumns_IsNoOp()
    {
        var cache = new NetezzaMetadataCache();
        var schema = new JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider();
        var live = new LiveMetadataSchemaProvider(cache, schema);
        var epoch = schema.MetadataEpoch;

        live.EnsureColumns(new TableInfo("T", "S", "D", Columns: []));

        Assert.Equal(epoch, schema.MetadataEpoch);
        Assert.False(cache.TryGetTable("D", "S", "T", out _));
    }

    [Fact]
    public void EnsureColumns_WithColumns_PublishesMergedTable()
    {
        var cache = new NetezzaMetadataCache();
        var schema = new JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider();
        var live = new LiveMetadataSchemaProvider(cache, schema);

        live.EnsureColumns(new TableInfo("T", "S", "D", Columns: [new ColumnInfo("ID")]));

        Assert.True(cache.TryGetTable("D", "S", "T", out var table));
        Assert.Single(table.Columns!);
        Assert.NotNull(schema.GetTable("D", "S", "T"));
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Condition not met within {timeout.TotalMilliseconds:0}ms.");
            }

            Thread.Yield();
        }
    }
}
