using System.Collections.Concurrent;
using JustyBase.Common.Contracts;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services.Documents;

namespace JustyBase.Services.Schema;

public sealed record SourceUsageResult(
    IReadOnlyList<SourceImpactNode> Nodes,
    bool Truncated,
    int ObjectsScanned,
    IReadOnlyList<string> SkippedDatabases,
    DateTimeOffset IndexedAt);

public sealed record SourceSearchResult(
    IReadOnlyList<SourceTextHit> Hits,
    IReadOnlyList<string> SkippedDatabases,
    DateTimeOffset IndexedAt);

/// <summary>
/// Builds and caches the definition index per connection. The catalog text is read
/// once and reused for 15 minutes; a schema refresh or an explicit refresh drops the
/// snapshot. The index itself is built off the UI thread.
/// </summary>
public sealed class SourceIndexService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);
    private const int DefaultImpactNodes = 500;
    private const int MaxImpactNodes = 2000;
    private const int MaxImpactDepth = 6;
    private const int DefaultSearchLimit = 2000;

    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _logger;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _buildLock = new(1, 1);

    public SourceIndexService(
        IGeneralApplicationData generalApplicationData,
        ISimpleLogger logger,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _logger = logger;
        _databaseServiceResolver = databaseServiceResolver;
    }

    public async Task<SourceIndex> GetIndexAsync(
        string connectionName,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!refresh && TryGetCached(connectionName, out var cached))
        {
            return cached!;
        }

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && TryGetCached(connectionName, out cached))
            {
                return cached!;
            }

            var index = await BuildIndexAsync(connectionName, cancellationToken).ConfigureAwait(false);
            _cache[connectionName] = new CacheEntry(index, DateTimeOffset.UtcNow);
            return index;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    public async Task<SourceUsageResult> GetUsagesAsync(
        string connectionName,
        SourceTarget target,
        int depth = 1,
        int maxNodes = DefaultImpactNodes,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var index = await GetIndexAsync(connectionName, refresh, cancellationToken).ConfigureAwait(false);
        var (nodes, truncated) = index.Impact(
            target,
            Math.Clamp(depth, 1, MaxImpactDepth),
            Math.Clamp(maxNodes, 1, MaxImpactNodes));

        return new SourceUsageResult(
            nodes,
            truncated,
            index.Count,
            index.SkippedDatabases,
            index.BuiltAt);
    }

    public async Task<SourceSearchResult> SearchAsync(
        string connectionName,
        SourceTextQuery query,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var index = await GetIndexAsync(connectionName, refresh, cancellationToken).ConfigureAwait(false);
        var effective = query.Limit > 0 ? query : new SourceTextQuery
        {
            Term = query.Term,
            CaseSensitive = query.CaseSensitive,
            WholeWord = query.WholeWord,
            Regex = query.Regex,
            IgnoreComments = query.IgnoreComments,
            ObjectTypes = query.ObjectTypes,
            Schemas = query.Schemas,
            Limit = DefaultSearchLimit,
        };

        return new SourceSearchResult(
            index.Search(effective),
            index.SkippedDatabases,
            index.BuiltAt);
    }

    /// <summary>Forgets the snapshot, e.g. after the schema was refreshed.</summary>
    public void Invalidate(string connectionName) => _cache.TryRemove(connectionName, out _);

    public void InvalidateAll() => _cache.Clear();

    private bool TryGetCached(string connectionName, out SourceIndex? index)
    {
        if (_cache.TryGetValue(connectionName, out var entry)
            && DateTimeOffset.UtcNow - entry.BuiltAt < CacheLifetime)
        {
            index = entry.Index;
            return true;
        }

        index = null;
        return false;
    }

    private async Task<SourceIndex> BuildIndexAsync(string connectionName, CancellationToken cancellationToken)
    {
        var databaseService = await Task.Run(
                () => _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName),
                cancellationToken)
            .ConfigureAwait(false);

        if (databaseService is null)
        {
            throw new InvalidOperationException($"Connection '{connectionName}' is not available.");
        }

        var dialect = SqlDialectResolver.ForDatabaseType(databaseService.DatabaseType);
        if (!databaseService.GetDatabases("").Any())
        {
            // The source index can be requested before the schema tree has loaded;
            // the catalog caches must exist before GetSchemas/GetDbObjects answer.
            await Task.Run(() => databaseService.CacheMainDictionary(), cancellationToken)
                .ConfigureAwait(false);
        }

        var types = new List<TypeInDatabaseEnum>
        {
            TypeInDatabaseEnum.Procedure,
            TypeInDatabaseEnum.View,
        };
        if (databaseService.IsTypeInDatabaseSupported(TypeInDatabaseEnum.ExternalTable))
        {
            types.Add(TypeInDatabaseEnum.ExternalTable);
        }

        await databaseService.CacheAllObjects(types.ToArray()).ConfigureAwait(false);

        return await Task.Run(
                () => BuildFromCatalog(databaseService, dialect, types, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private SourceIndex BuildFromCatalog(
        IDatabaseService databaseService,
        SqlDialect dialect,
        IReadOnlyList<TypeInDatabaseEnum> types,
        CancellationToken cancellationToken)
    {
        var entries = new List<SourceDocument>();
        var skipped = new List<string>();

        foreach (var database in databaseService.GetDatabases(""))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var schema in databaseService.GetSchemas(database, ""))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var type in types)
                    {
                        foreach (var databaseObject in databaseService.GetDbObjects(database, schema, "", type))
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return SourceIndex.Build(entries, dialect, skipped);
                            }

                            var source = databaseService.GetItemSource(
                                type,
                                database,
                                schema,
                                databaseObject.Name,
                                databaseObject.Id);
                            if (string.IsNullOrWhiteSpace(source))
                            {
                                continue;
                            }

                            entries.Add(new SourceDocument(
                                database,
                                schema,
                                databaseObject.Name,
                                null,
                                ObjectTypeName(type),
                                source));
                        }
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OutOfMemoryException)
            {
                skipped.Add(database);
                _logger.TrackError(ex, isCrash: false);
            }
        }

        return SourceIndex.Build(entries, dialect, skipped);
    }

    private static string ObjectTypeName(TypeInDatabaseEnum type)
        => type == TypeInDatabaseEnum.ExternalTable
            ? "EXTERNAL TABLE"
            : type.ToString().ToUpperInvariant();

    private sealed record CacheEntry(SourceIndex Index, DateTimeOffset BuiltAt);
}
