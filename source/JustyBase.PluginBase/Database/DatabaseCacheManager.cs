using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Models;
using System.Data.Common;
using System.Runtime.InteropServices;

namespace JustyBase.PluginDatabaseBase.Database;

internal sealed class DatabaseCacheManager
{
    private static readonly Lock CacheLock = new();

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>> _databaseSchemaTable;
    private readonly Dictionary<string, string> _databaseDefSchema;

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>> _procedureDictCache;
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>> _viewDictCache;
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>> _synonymTableDictCache;

    private bool _initialized;

    public DatabaseCacheManager(
        Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>> databaseSchemaTable,
        Dictionary<string, string> databaseDefSchema,
        Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>> procedureDictCache,
        Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>> viewDictCache,
        Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>> synonymTableDictCache)
    {
        _databaseSchemaTable = databaseSchemaTable;
        _databaseDefSchema = databaseDefSchema;
        _procedureDictCache = procedureDictCache;
        _viewDictCache = viewDictCache;
        _synonymTableDictCache = synonymTableDictCache;
    }

    public void CacheMainDictionary(
        DatabaseTypeEnum databaseType,
        Func<List<(string databaseName, string defaultSchema)>> getDatabases,
        Action disposeSharedConnection,
        Func<string?, bool, DbConnection> getConnection,
        Action<string, DbConnection> loadDatabaseObject,
        Action<string, DbConnection> loadColumns,
        Action<DatabaseConnectedLevel> setConnectedLevel,
        INetezza? netezza,
        ISimpleLogger? logger,
        Action<DbConnection>? configureConnection = null)
    {
        if (_initialized)
        {
            return;
        }

        lock (CacheLock)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            List<(string, string)> databasesList = getDatabases();
            foreach (var (database, defSchema) in databasesList)
            {
                _databaseSchemaTable[database] = new(StringComparer.OrdinalIgnoreCase);
                _databaseDefSchema[database] = defSchema;
            }

            disposeSharedConnection();

            Parallel.ForEach(databasesList, new ParallelOptions { MaxDegreeOfParallelism = 8 }, o =>
            {
                var (database, _) = o;
                if (databaseType == DatabaseTypeEnum.PostgreSql && database == "template0")
                {
                    return;
                }

                var con = getConnection(database, false);
                try
                {
                    con.Open();
                    configureConnection?.Invoke(con);
                    loadDatabaseObject(database, con);
                    setConnectedLevel(DatabaseConnectedLevel.ConnectedDatabaseObjects);

                    if (databaseType == DatabaseTypeEnum.PostgreSql)
                    {
                        con = ResetConnection(database, con, getConnection, configureConnection);
                    }

                    loadColumns(database, con);

                    if (netezza is not null)
                    {
                        netezza.FillDistInfoForDatabase(database, con);
                        netezza.FillKeysInfoForDatabase(database, con);
                    }

                    setConnectedLevel(DatabaseConnectedLevel.ConnectedColumns);
                    con.Close();
                }
                catch (Exception ex)
                {
                    if (!ex.Message.StartsWith("55000", StringComparison.Ordinal))
                    {
                        logger?.TrackError(ex, isCrash: false);
                    }
                }
                finally
                {
                    con.Dispose();
                }
            });
        }
    }

    public Task CacheAllObjects(
        TypeInDatabaseEnum[] typeInDatabaseArr,
        string databaseName,
        string procedureName,
        Func<string, IEnumerable<string>> getDatabases,
        Func<string?, bool, DbConnection> getConnection,
        Func<DbConnection, DbCommand> createCommandFromConnection,
        Func<TypeInDatabaseEnum, string, string, string?> getObjectCode,
        Func<TypeInDatabaseEnum, bool> isTypeInDatabaseSupported,
        INetezza? netezza,
        ISimpleLogger? logger,
        Action<DbConnection>? configureConnection = null)
    {
        return Task.Run(() =>
        {
            bool cacheProcedures = typeInDatabaseArr.Contains(TypeInDatabaseEnum.Procedure);
            bool cacheViews = typeInDatabaseArr.Contains(TypeInDatabaseEnum.View);
            bool cacheSynonyms = typeInDatabaseArr.Contains(TypeInDatabaseEnum.Synonym);

            // Keep the last complete source snapshot visible while the refresh is
            // reading databases. A failed refresh must not publish an empty or
            // partial procedure/view/synonym cache.
            var stagedProcedures = new Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>>();
            var stagedViews = new Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>>();
            var stagedSynonyms = new Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>>();
            int failures = 0;

            // External-table data is owned by the Netezza implementation and has no
            // snapshot API in the legacy contract, so retain its existing invalidation
            // behavior. The other source types use the staged commit below.
            lock (CacheLock)
            {
                Action? clearExternalTableCache = netezza is null ? null : netezza.ClearExternalTableCache;
                if (typeInDatabaseArr.Contains(TypeInDatabaseEnum.ExternalTable))
                    clearExternalTableCache?.Invoke();
            }

            Parallel.ForEach(getDatabases(databaseName), new ParallelOptions { MaxDegreeOfParallelism = 4 }, database =>
            {
                try
                {
                    using var con = getConnection(database, false);
                    con.Open();
                    configureConnection?.Invoke(con);

                    foreach (var typeInDatabase in typeInDatabaseArr)
                    {
                        if (!isTypeInDatabaseSupported(typeInDatabase))
                        {
                            continue;
                        }

                        try
                        {
                            var cmd = createCommandFromConnection(con);
                            cmd.CommandText = getObjectCode(typeInDatabase, database, procedureName);
                            var rdr = cmd.ExecuteReader();

                            if (typeInDatabase == TypeInDatabaseEnum.Procedure)
                            {
                                while (rdr.Read())
                                {
                                    string? schema = rdr.GetString(0);
                                    string? source = rdr.GetValue(1) as string ?? "";
                                    int id = rdr.GetInt32(2);

                                    string? returns = rdr.GetValue(3) as string;
                                    if (netezza is not null)
                                    {
                                        returns = netezza.NetezzazProcWrongReturnFix(returns);
                                    }

                                    object executeAsOwnerObj = rdr.GetValue(4);
                                    bool executedAsOwner = executeAsOwnerObj switch
                                    {
                                        bool b => b,
                                        short s => s == 1,
                                        _ => false,
                                    };

                                    object procedureSignatureObj = rdr.GetValue(6);

                                    if (procedureSignatureObj is string procedureSignature)
                                    {
                                        string? stringDesc = rdr.GetValue(5) as string;
                                        string? arguments = rdr.GetValue(7) as string;
                                        string? language = rdr.GetValue(8) as string;

                                        lock (stagedProcedures)
                                        {
                                            ref var databaseItem = ref CollectionsMarshal.GetValueRefOrAddDefault(stagedProcedures, database, out _);
                                            databaseItem ??= [];
                                            ref var schemaItem = ref CollectionsMarshal.GetValueRefOrAddDefault(databaseItem, schema, out _);
                                            schemaItem ??= [];
                                            schemaItem[procedureSignature] = new ProcedureCachedInfo()
                                            {
                                                Id = id,
                                                ProcedureSource = source,
                                                Returns = returns,
                                                ExecuteAsOwner = executedAsOwner,
                                                Desc = stringDesc,
                                                ProcedureSignature = procedureSignature,
                                                Arguments = arguments,
                                                ProcLanguage = language,
                                            };

                                        }
                                    }
                                }
                            }
                            else if (typeInDatabase == TypeInDatabaseEnum.View)
                            {
                                while (rdr.Read())
                                {
                                    string? schema = rdr.GetValue(0) as string;
                                    string? viewName = rdr.GetString(1);
                                    string? source = rdr.GetString(2);

                                    lock (stagedViews)
                                    {
                                        ref var databaseItem = ref CollectionsMarshal.GetValueRefOrAddDefault(stagedViews, database, out _);
                                        databaseItem ??= [];
                                        ref var schemaItem = ref CollectionsMarshal.GetValueRefOrAddDefault(databaseItem!, schema!, out _);
                                        schemaItem ??= [];
                                        schemaItem[viewName] = new ViewCachedInfo(source);
                                    }
                                }
                            }
                            else if (typeInDatabase == TypeInDatabaseEnum.ExternalTable && netezza is not null)
                            {
                                netezza.ReadExternalTable(database, rdr);
                            }
                            else if (typeInDatabase == TypeInDatabaseEnum.Synonym)
                            {
                                while (rdr.Read())
                                {
                                    string? schema = rdr.GetValue(0) as string;
                                    string? name = rdr.GetString(1);
                                    string refObjName = rdr.GetString(2);
                                    string refObjNamePart1 = rdr.GetValue(3) as string ?? "PROBLEM";
                                    string refObjNamePart2 = rdr.GetValue(4) as string ?? "PROBLEM";

                                    lock (stagedSynonyms)
                                    {
                                        ref var databaseItem = ref CollectionsMarshal.GetValueRefOrAddDefault(stagedSynonyms, database, out _);
                                        databaseItem ??= [];
                                        ref var schemaItem = ref CollectionsMarshal.GetValueRefOrAddDefault(databaseItem!, schema!, out _);
                                        schemaItem ??= [];
                                        var syn = new SynonymCachedInfo(refObjNamePart1, refObjNamePart2, refObjName);
                                        schemaItem[name] = syn;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failures);
                            logger?.TrackError(ex, isCrash: false);
                        }
                    }

                    Task t1 = Task.Run(() =>
                    {
                        try
                        {
                            con.Close();
                        }
                        catch
                        {
                            // Connection already torn down by the timeout path; close is best-effort.
                        }
                    });

                    Task t2 = Task.Delay(2_000);
                    Task.WaitAny(t1, t2);
                    _ = t1.ContinueWith(static x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failures);
                    logger?.TrackError(ex, isCrash: false);
                }
            });

            if (Volatile.Read(ref failures) == 0)
            {
                lock (CacheLock)
                {
                    if (cacheProcedures)
                        ReplaceCache(_procedureDictCache, stagedProcedures);
                    if (cacheViews)
                        ReplaceCache(_viewDictCache, stagedViews);
                    if (cacheSynonyms)
                        ReplaceCache(_synonymTableDictCache, stagedSynonyms);
                }
            }
        });
    }

    private static void ReplaceCache<TValue>(
        Dictionary<string, Dictionary<string, Dictionary<string, TValue>>> target,
        Dictionary<string, Dictionary<string, Dictionary<string, TValue>>> staged)
    {
        lock (target)
        {
            target.Clear();
            foreach (var (database, schemas) in staged)
                target[database] = schemas;
        }
    }

    public void ClearMainCache()
    {
        lock (CacheLock)
        {
            _databaseSchemaTable.Clear();
            _databaseDefSchema.Clear();
            _initialized = false;
        }
    }

    private void ClearCache(TypeInDatabaseEnum[] typeInDatabaseArr, Action? clearExternalTableCache)
    {
        DatabaseCacheInvalidationHelper.ClearCaches(
            typeInDatabaseArr,
            _procedureDictCache,
            _viewDictCache,
            _synonymTableDictCache,
            clearExternalTableCache);
    }

    private static DbConnection ResetConnection(
        string database,
        DbConnection con,
        Func<string?, bool, DbConnection> getConnection,
        Action<DbConnection>? configureConnection)
    {
        con.Close();
        con.Dispose();
        var newConn = getConnection(database, false);
        newConn.Open();
        configureConnection?.Invoke(newConn);
        return newConn;
    }
}
