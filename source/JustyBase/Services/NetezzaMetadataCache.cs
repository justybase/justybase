using System.Collections.Concurrent;
using JustyBase.Netezza.Metadata;
using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.Services;

/// <summary>
/// In-memory metadata cache with TTL and merge-before-replace semantics (Lite contract subset).
/// </summary>
public sealed class NetezzaMetadataCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _tables = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;

    public int Generation => Volatile.Read(ref _generation);

    public void BumpGeneration() => Interlocked.Increment(ref _generation);

    public void MergeTable(TableInfo table, TimeSpan ttl)
    {
        var key = TableKey(table);
        var expires = DateTime.UtcNow.Add(ttl);
        _tables.AddOrUpdate(
            key,
            _ => new CacheEntry(table, expires),
            (_, existing) =>
            {
                var merged = MergeTable(existing.Table, table);
                return new CacheEntry(merged, expires);
            });
    }

    public bool TryGetTable(string? database, string? schema, string name, out TableInfo table)
    {
        table = null!;
        if (!_tables.TryGetValue(TableKey(database ?? string.Empty, schema ?? string.Empty, name), out var entry))
            return false;

        if (entry.ExpiresUtc < DateTime.UtcNow)
        {
            _tables.TryRemove(TableKey(database ?? string.Empty, schema ?? string.Empty, name), out _);
            return false;
        }

        table = entry.Table;
        return true;
    }

    public IReadOnlyList<TableInfo> GetFreshTables()
    {
        var now = DateTime.UtcNow;
        var result = new List<TableInfo>();
        foreach (var (key, entry) in _tables)
        {
            if (entry.ExpiresUtc < now)
            {
                _tables.TryRemove(key, out _);
                continue;
            }
            result.Add(entry.Table);
        }
        return result;
    }

    public void Clear() => _tables.Clear();

    private static string TableKey(TableInfo table)
        => TableKey(table.Database ?? string.Empty, table.Schema ?? string.Empty, table.Name);

    private static string TableKey(string database, string schema, string name)
        => $"{database}|{schema}|{name}";

    private static TableInfo MergeTable(TableInfo existing, TableInfo incoming)
    {
        var columns = incoming.Columns is { Count: > 0 } incomingColumns
            ? incomingColumns
            : existing.Columns;

        return existing with
        {
            Columns = columns,
            IsView = incoming.IsView || existing.IsView
        };
    }

    private sealed record CacheEntry(TableInfo Table, DateTime ExpiresUtc);
}

/// <summary>Bridges <see cref="NetezzaMetadataCache"/> into <see cref="JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider"/>.</summary>
public sealed class LiveMetadataSchemaProvider
{
    private readonly NetezzaMetadataCache _cache;
    private readonly JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider _schemaProvider;

    public LiveMetadataSchemaProvider(
        NetezzaMetadataCache cache,
        JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider schemaProvider)
    {
        _cache = cache;
        _schemaProvider = schemaProvider;
    }

    /// <summary>
    /// Merges into the TTL cache and publishes the <em>merged</em> table to the schema provider
    /// so deferred (empty) column snapshots do not wipe previously hydrated columns.
    /// </summary>
    /// <param name="bumpEpoch">
    /// When <see langword="false"/>, skips generation/epoch bumps — call <see cref="PublishEpochBump"/>
    /// once after a batch (e.g. full schema sync).
    /// </param>
    public void MergeAndPublish(TableInfo table, TimeSpan? ttl = null, bool bumpEpoch = true)
    {
        _cache.MergeTable(table, ttl ?? MetadataPrefetchContract.DefaultTtl);

        var published = _cache.TryGetTable(table.Database, table.Schema, table.Name, out var merged)
            ? merged
            : table;
        _schemaProvider.AddTable(published);

        if (bumpEpoch)
            PublishEpochBump();
    }

    /// <summary>Advances cache generation and schema metadata epoch once (after a batch publish).</summary>
    public void PublishEpochBump()
    {
        _cache.BumpGeneration();
        _schemaProvider.BumpMetadataEpoch();
    }

    /// <summary>
    /// Ensure columns for a specific table are present (lazy hydration for ≥500 objects).
    /// </summary>
    public void EnsureColumns(TableInfo tableWithColumns, TimeSpan? ttl = null)
    {
        if (tableWithColumns.Columns is not { Count: > 0 }) return;
        MergeAndPublish(tableWithColumns, ttl);
    }

    public void Clear()
    {
        _cache.Clear();
        _schemaProvider.Clear();
        PublishEpochBump();
    }
}
