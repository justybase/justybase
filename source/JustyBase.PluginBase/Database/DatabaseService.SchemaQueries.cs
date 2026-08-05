using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Models;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace JustyBase.PluginDatabaseBase.Database;

public abstract partial class DatabaseService
{
    protected readonly Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>> _databaseSchemaTable = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _databaseDefSchema = new(StringComparer.OrdinalIgnoreCase);

    //DatabaseSchemaTableColumn[database][schema][objectName][column]

    private readonly Dictionary<string, Dictionary<int, ColumnInterval>> DatabaseTableIdColumnIntervalSpan = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DatabaseColumn[]> DatabaseColumnsList = new Dictionary<string, DatabaseColumn[]>(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> GetDatabases(string filter)
    {
        foreach (var item in _databaseSchemaTable.Keys)
        {
            if (DatabaseFilterHelper.MatchesPrefixOrUnderscore(item, filter))
            {
                yield return item;
            }
        }
    }
    public IEnumerable<string> GetSchemas(string database, string filter)
    {
        var resolvedDatabase = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(database, _databaseSchemaTable.Keys);
        if (resolvedDatabase is null)
        {
            yield break;
        }
        if (_databaseSchemaTable.TryGetValue(resolvedDatabase, out var pairs))
        {
            foreach (var item in pairs.Keys)
            {
                if (DatabaseFilterHelper.MatchesPrefixOrUnderscore(item, filter))
                {
                    yield return item;
                }
            }
        }
    }

    public IEnumerable<(DatabaseObject dbObject, string schema)> FindDbObject(string database, string schema, string name, bool cleanNames)
    {
        if (cleanNames)
        {
            database = CleanSqlWord(database, AutoCompletDatabaseMode);
            schema = CleanSqlWord(schema, AutoCompletDatabaseMode);
            name = CleanSqlWord(name, AutoCompletDatabaseMode);
        }

        var resolvedDatabase = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(database, _databaseSchemaTable.Keys);
        if (resolvedDatabase is not null && _databaseSchemaTable.TryGetValue(resolvedDatabase, out var pairs))
        {
            foreach (var item in pairs)
            {
                if ((string.IsNullOrEmpty(schema) || item.Key.Equals(schema, StringComparison.OrdinalIgnoreCase))
                    && name is not null && item.Value.TryGetValue(name, out var res))
                {
                    yield return (res, item.Key);
                }
            }
        }
    }

    private List<string> GetAvailableSchemas(string database, string? schema)
        => DatabaseSchemaResolver.GetAvailableSchemas(database, schema, _databaseDefSchema, _databaseSchemaTable);
    public IEnumerable<DatabaseObject> GetDbObjects(string database, string schema, string filter, TypeInDatabaseEnum typeInDatabase)
    {
        var resolvedDatabase = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(database, _databaseSchemaTable.Keys);
        if (resolvedDatabase is null)
        {
            yield break;
        }
        if (_databaseSchemaTable.TryGetValue(resolvedDatabase, out var pairs))
        {
            List<string> schemas = GetAvailableSchemas(resolvedDatabase, schema);
            foreach (var schemaX in schemas)
            {
                if (schemaX is not null && pairs.TryGetValue(schemaX, out var strings))
                {
                    foreach (var (_, item) in strings)
                    {
                        var itemName = item.Name;
                        if (DatabaseFilterHelper.MatchesPrefixOrUnderscore(itemName, filter))
                        {
                            if (typeInDatabase == TypeInDatabaseEnum.allObjects || item.TypeInDatabase == typeInDatabase)
                            {
                                yield return item;
                            }
                        }
                    }
                }
            }
        }
    }

    public IEnumerable<DatabaseColumn> GetColumns(string? database, string? schema, string? table, string filter)
    {
        if (table is null)
        {
            yield break;
        }
        var resolvedDatabase = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(database, _databaseSchemaTable.Keys);
        if (resolvedDatabase is null)
        {
            yield break;
        }

        List<string> schemas = GetAvailableSchemas(resolvedDatabase, schema);

        if (schemas.Count == 0)
        {
            yield break;
        }

        if (_databaseSchemaTable.TryGetValue(resolvedDatabase, out var schemaTableDict))
        {
            foreach (var schemaX in schemas)
            {
                if (schemaX is not null && schemaTableDict.TryGetValue(schemaX, out var tableDictionary))
                {
                    if (tableDictionary.TryGetValue(table, out var accualObject) &&
                        DatabaseTableIdColumnIntervalSpan.TryGetValue(resolvedDatabase, out var columnDictionaryOfCurrentDatabase) &&
                        columnDictionaryOfCurrentDatabase.TryGetValue(accualObject.Id, out var columnInterval))
                    {
                        int firstColumnIndex = columnInterval.FirstIndex;
                        int lastColumnIndex = columnInterval.LastIndex;

                        if (DatabaseColumnsList.TryGetValue(resolvedDatabase, out var columnsArray))
                        {
                            for (int i = firstColumnIndex; i < lastColumnIndex && i < columnsArray.Length; i++)
                            {
                                var item = columnsArray[i];
                                if (DatabaseFilterHelper.MatchesContains(item.Name, filter))
                                {
                                    yield return columnsArray[i];
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public IEnumerable<(DatabaseColumn, DatabaseObject)> GetColumnsFromAllTablesAndSchemas(string database, string schema)
    {
        foreach (var table in GetDbObjects(database, schema, "", TypeInDatabaseEnum.Table))
        {
            foreach (var item in GetColumns(database, schema, table.Name, ""))
            {
                yield return (item, table);
            }
        }
        foreach (var table in GetDbObjects(database, schema, "", TypeInDatabaseEnum.View))
        {
            foreach (var item in GetColumns(database, schema, table.Name, ""))
            {
                yield return (item, table);
            }
        }
    }

    protected Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>> _procedureDictCache = [];

    protected Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>> _viewDictCache = [];

    protected Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>> _synonymTableDictCache = [];

    protected abstract string? GetProceduresSql(string database, string objectFilterName);

    protected virtual string? GetViewsSql(string database, string objectFilterName)
    {
        return "select table_schema,table_name, view_definition from information_schema.views";
    }

    protected abstract string? GetExternalTableSql(string database);

    protected abstract string? GetSynonymSql(string database);

    protected string? GetObjectCode(TypeInDatabaseEnum typeInDatabase, string database, string objectFilterName = "")
    {
        return typeInDatabase switch
        {
            TypeInDatabaseEnum.Procedure => GetProceduresSql(database, objectFilterName),
            TypeInDatabaseEnum.View => GetViewsSql(database, objectFilterName),
            TypeInDatabaseEnum.ExternalTable => GetExternalTableSql(database),
            TypeInDatabaseEnum.Synonym => GetSynonymSql(database),
            _ => null,
        };
    }

    protected (string, string, string) GetCleanedNames(string database, string schema, string tableName)
        => DatabaseSchemaResolver.GetCleanedNames(database, schema, tableName, QuoteNameIfNeeded);

    protected (string, string) GetCleanedNames(string schema, string tableName)
        => DatabaseSchemaResolver.GetCleanedNames(schema, tableName, QuoteNameIfNeeded);
    protected static string? CleanComment(string comment)
    {
        if (comment is null)
        {
            return null;
        }
        else
        {
            return comment.Replace("'", "''");
        }
    }
    public virtual bool IsTypeInDatabaseSupported(TypeInDatabaseEnum typeInDatabase)
    {
        return typeInDatabase != TypeInDatabaseEnum.ExternalTable && typeInDatabase != TypeInDatabaseEnum.Synonym;
    }

    public async Task CacheAllObjects(TypeInDatabaseEnum[] typeInDatabaseArr, string databaseName = "", string procedureName = "")
    {
        await _cacheManager.CacheAllObjects(
            typeInDatabaseArr,
            databaseName,
            procedureName,
            getDatabases: GetDatabases,
            getConnection: GetConnection,
            createCommandFromConnection: CreateCommandFromConnection,
            getObjectCode: GetObjectCode,
            isTypeInDatabaseSupported: IsTypeInDatabaseSupported,
            netezza: this as INetezza,
            logger: Logger);
    }

    private string? GetProcedureSource(string database, string schema, string procedureName, int procedureId)
    {
        string? res = null;
        if (database is not null && _procedureDictCache.TryGetValue(database, out var schemas)
            && schema is not null && schemas.TryGetValue(schema, out var procedures)
            && procedureName is not null && procedures.TryGetValue(procedureName, out var procedure))
        {
            if (procedure.Id == procedureId || procedure.Id == -1)
            {
                res = procedure.ProcedureSource;
            }
        }
        return res;
    }

    public bool IsItemSourceContains(TypeInDatabaseEnum typeInDatabase, string database, string schema, string itemNameOrSignature, int procedureId, StringComparison comp, string searchWord, Regex rx)
    {
        string? res = "";
        if (database is not null)
        {
            switch (typeInDatabase)
            {
                case TypeInDatabaseEnum.Procedure:
                    res = GetProcedureSource(database, schema, itemNameOrSignature, procedureId);
                    break;
                case TypeInDatabaseEnum.View:
                    res = GetViewSource(database, schema, itemNameOrSignature);
                    break;
                case TypeInDatabaseEnum.ExternalTable:
                    if (this is INetezza netezza)
                    {
                        res = netezza.GetExternalDataObject(database, schema, itemNameOrSignature);
                    }
                    break;
                case TypeInDatabaseEnum.Synonym:
                    if (_synonymTableDictCache.TryGetValue(database, out var tmp1) && schema is not null && tmp1.TryGetValue(schema, out var tmp2)
                        && itemNameOrSignature is not null && tmp2.TryGetValue(itemNameOrSignature, out var finalX))
                    {
                        res = finalX.RefObjNamePart3;
                    }
                    break;
                default:
                    break;
            }
        }
        if (string.IsNullOrEmpty(res))
        {
            return false;
        }

        if (rx is not null)
        {
            return rx.IsMatch(res);
        }
        else
        {
            return res.Contains(searchWord, comp);
        }
    }

    public virtual async ValueTask<List<ProcedureCachedInfo>> GetProceduresSignaturesFromName(string database, string schema, string procName)
    {
        await Task.CompletedTask;
        return [new ProcedureCachedInfo() { ProcedureSignature = String.Empty }];
    }
    private string? GetViewSource(string database, string schema, string procedureName)
    {
        string? res = null;
        if (database is not null && _viewDictCache.TryGetValue(database, out var schemas)
            && schema is not null && schemas.TryGetValue(schema, out var procedures)
            && procedureName is not null && procedures.TryGetValue(procedureName, out var view)
            )
        {
            res = view.ViewSource;
        }
        return res;
    }

    protected abstract string GetSqlTablesAndOtherObjects(string dbName);
    protected abstract string GetSqlOfColumns(string dbName);

    private void LoadDatabaseObject(string database, DbConnection con)
    {
        var cmd = CreateCommandFromConnection(con);
        cmd.CommandText = GetSqlTablesAndOtherObjects(database);
        var rdr = cmd.ExecuteReader();
        var acualDb = _databaseSchemaTable[database];
        while (rdr.Read())
        {
            int objId = rdr.GetInt32(0);
            string objNme = rdr.GetString(1);
            string? desc = rdr.GetValue(2) as string;
            string schema = rdr.GetString(3);
            string databaseObjectType = rdr.GetString(4);
            string owner = rdr.GetString(5);
            DateTime? crtTime = rdr.GetValue(6) as DateTime?;
            TypeInDatabaseEnum dbType = databaseObjectType.GetTypeInDatabaseEnumFromDbName();

            _ = acualDb.TryAdd(schema, []); // no StringComparer.OrdinalIgnoreCase by purpouse
            acualDb[schema][objNme] = new DatabaseObject(objId, objNme, desc, dbType, databaseObjectType, owner, crtTime);
        }
        if (DatabaseType == DatabaseTypeEnum.PostgreSql)
        {
            cmd.Dispose();
        }
        if (DatabaseType == DatabaseTypeEnum.MySql)
        {
            rdr.Close();
        }
    }

    private void LoadColumns(string database, DbConnection con)
    {
        var currentDic = new Dictionary<int, ColumnInterval>();

        var cmd = CreateCommandFromConnection(con);

        cmd.CommandText = GetSqlOfColumns(database);
        var rdr = cmd.ExecuteReader();

        List<DatabaseColumn> tempCols = [];
        int num = 0;
        int prevObjId = -1;
        int tmpA = 0;

        while (rdr.Read())
        {
            string columnName = rdr.GetString(1);
            int obejctId = rdr.GetInt32(0);

            string? desc = rdr.GetValue(2) as string;
            string columnTypeFullName = rdr.GetString(3);

            var notNull = rdr.GetValue(4);
            bool columnNotNull = false;
            if (notNull is bool boolNotNull) // false/true
            {
                columnNotNull = boolNotNull;
            }
            else if (notNull is int intNotNull) // 0/1
            {
                columnNotNull = intNotNull > 0;
            }

            if (prevObjId != -1 && prevObjId != obejctId)
            {
                currentDic[prevObjId] = new ColumnInterval() { FirstIndex = tmpA, LastIndex = num };
                tmpA = num;
            }
            prevObjId = obejctId;
            string? colDefValue = rdr.GetValue(5) as string;

            tempCols.Add(new DatabaseColumn(columnName, desc, columnTypeFullName, columnNotNull, colDefValue));
            num++;
        }
        currentDic[prevObjId] = new ColumnInterval() { FirstIndex = tmpA, LastIndex = num };
        lock (_lock2)
        {
            DatabaseTableIdColumnIntervalSpan[database] = currentDic;
            DatabaseColumnsList[database] = tempCols.ToArray();
        }
        if (DatabaseType == DatabaseTypeEnum.PostgreSql)
        {
            cmd.Dispose();
        }
    }
    protected abstract List<(string databaseName, string defaultSchema)> GetDatabases();

}
