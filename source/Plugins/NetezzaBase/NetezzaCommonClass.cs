using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Text;
using JustyBase.Helpers.NetezzaImporter;
using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.Models;
using JustyBase.NetezzaCatalogSql;
using JustyBase.NetezzaDdl;
using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.PluginDatabaseBase.Models;

namespace NetezzaBase;

public class NetezzaCommonClass : DatabaseService, INetezza
{
    private readonly NetezzaDdlBuilder _ddlBuilder;
    private readonly NetezzaDdlTextBuilder _ddlTextBuilder = new();

    public NetezzaCommonClass(string username, string password, string port, string ip, string db, int connectionTimeout) : base(username, password, port, ip, db, connectionTimeout)
    {
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.DatabaseSchemaTable |
            CurrentAutoCompletDatabaseMode.SchemaOptional |
            CurrentAutoCompletDatabaseMode.SchemaTable |
            CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional |
            CurrentAutoCompletDatabaseMode.NullSchemaCanBeAccepted |
            CurrentAutoCompletDatabaseMode.MakeUpperCase;

        _ddlBuilder = new NetezzaDdlBuilder(
            (database, schema, table) => GetQuotedTwoOrTreePartName(database, schema, table),
            GetCleanedNames,
            GetColumns,
            QuoteNameIfNeeded);
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        throw new NotImplementedException();
    }


    public override void ChangeDatabaseSpecial(DbConnection con, string databaseName)
    {
        try
        {
            if (con.State != ConnectionState.Open)
            {
                con.Open();
            }
            var cmdX = con.CreateCommand();
            cmdX.CommandText = NetezzaSystemSql.SetCatalog(databaseName);
            cmdX.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            if (ex.Message?.StartsWith("Failed to establish a connection to ") == false)
            {
                con.ChangeDatabase(databaseName);
            }
            else
            {
                throw;
            }
        }
    }


    protected override List<(string, string)> GetDatabases()
    {
        var con = Connection;

        if (con.State != ConnectionState.Open)
        {
            con.Open();
        }
        var cmd = CreateCommandFromConnection(con);
        cmd.CommandText = NetezzaCatalogSql.DatabasesSql;
        var rdr = cmd.ExecuteReader();
        List<(string, string)> databases = [];
        while (rdr.Read())
        {
            databases.Add((rdr.GetString(2), rdr.GetString(4)));
        }
        return databases;
    }
    protected override string GetSqlTablesAndOtherObjects(string dbName)
    {
        return NetezzaCatalogSql.GetSqlTablesAndOtherObjects(dbName);
    }
    protected override string GetSqlOfColumns(string dbName)
    {
        return NetezzaCatalogSql.GetSqlOfColumns(dbName);
    }

    protected override string? GetProceduresSql(string database, string objectFilterName)
    {
        return NetezzaCatalogSql.GetProceduresSql(database, objectFilterName);
    }
    //returns ay..
    public string? NetezzazProcWrongReturnFix(string? procReturns)
        => procReturns is null ? null : NetezzaProcTypes.FixProcedureReturnType(procReturns);

    protected override string? GetSynonymSql(string database)
    {
        return NetezzaCatalogSql.GetSynonymSql(database);
    }

    protected override string? GetViewsSql(string database, string objectFilterName)
    {
        return NetezzaCatalogSql.GetViewsSql(database, objectFilterName);
    }

    protected override string? GetExternalTableSql(string database)
    {
        return NetezzaCatalogSql.GetExternalTableSql(database);
    }

    public override string GetDeleted(string table, string database, string schema)
    {
        return _ddlBuilder.GetDeleted(table, database, schema);
    }

    public override string GetGrant(string database, string schema, string table)
    {
        return _ddlBuilder.GetGrant(database, schema, table);
    }

    public override string GetOrganize(string database, string schema, string table)
    {
        return _ddlBuilder.GetOrganize(database, schema, table);
    }

    public override string GetGroom(string database, string schema, string table)
    {
        return _ddlBuilder.GetGroom(database, schema, table);
    }

    public override string GetGenerateStats(string database, string schema, string table)
    {
        return _ddlBuilder.GetGenerateStats(database, schema, table);
    }
    public override string GetAddComment(string table, string database, string schema)
    {
        return _ddlBuilder.GetAddComment(table, database, schema);
    }

    protected Dictionary<string, Dictionary<string, Dictionary<string, ExternaTableCachedInfo>>> _exteralTableDictCache = [];
    public void ClearExternalTableCache()
    {
        _exteralTableDictCache.Clear();
    }

    protected Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> _distributionDictionary = [];
    public Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> DistributionDictionary => _distributionDictionary;

    protected Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> _oraganizeDictionary = [];
    public Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> OrganizeDictionary => _oraganizeDictionary;


    protected readonly Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, NetezzaKeyItem>>>> keysDictionary = [];
    public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, NetezzaKeyItem>>>> KeysDictionary => keysDictionary;

    public void FillKeysInfoForDatabase(string databaseName, DbConnection? dbConnection = null)
    {
        lock (_lock2)
        {
            keysDictionary[databaseName] = [];
        }

        var databaseDic = keysDictionary[databaseName];

        var con = dbConnection ?? Connection;
        ChangeDatabaseSpecial(con, databaseName);
        if (con is not null && con.State != ConnectionState.Open)
        {
            con.Open();
        }
        var cmd = CreateCommandFromConnection(con);
        cmd.CommandText = NetezzaCatalogSql.GetKeysSql(databaseName);

        var rdr = cmd.ExecuteReader();

        while (rdr.Read())
        {
            //object schemaObj = rdr.GetValue(0);
            //string schema = schemaObj is null ? "ADMIN" : schemaObj.ToString();

            string schema = rdr.GetString(0);

            string tabName = rdr.GetString(1);
            string keyName = rdr.GetString(2);
            char keyType = rdr.GetString(3)[0];
            string? attName = rdr.GetValue(4) as string;

            string? PKDATABASE = rdr.GetValue(5) as string;
            string? PKSCHEMA = rdr.GetValue(6) as string;
            string? PKRELATION = rdr.GetValue(7) as string;
            string? PKATTNAME = rdr.GetValue(8) as string;
            string UPDT_TYPE = rdr.GetValue(9) as string ?? "NO ACTION";
            string DEL_TYPE = rdr.GetValue(10) as string ?? "NO ACTION";


            if (!databaseDic.TryGetValue(schema, out var databaseDictLevel1))
            {
                databaseDictLevel1 = [];
                databaseDic[schema] = databaseDictLevel1;
            }

            if (!databaseDictLevel1.TryGetValue(tabName, out var databaseDictLevel2))
            {
                databaseDictLevel2 = [];
                databaseDictLevel1[tabName] = databaseDictLevel2;
            }

            if (!databaseDictLevel2.TryGetValue(keyName, out var databaseDictLevel3))
            {
                databaseDictLevel3 = new NetezzaKeyItem(keyType, PKDATABASE, PKSCHEMA, PKRELATION, new List<(string colName, string referencedPkColName)>(), UPDT_TYPE, DEL_TYPE);
                databaseDictLevel2[keyName] = databaseDictLevel3;
            }

            if (attName is not null && PKATTNAME is not null)
            {
                databaseDictLevel3.ColumnList.Add((attName, PKATTNAME));
            }
        }
    }

    public async Task FillKeysInfoForDatabaseAsync(string databaseName, DbConnection? dbConnection = null)
    {
        await Task.Run(() => FillKeysInfoForDatabase(databaseName, dbConnection));
    }

    public void FillDistInfoForDatabase(string databaseName, DbConnection? dbConnection = null)
    {
        lock (_lock2)
        {
            DistributionDictionary[databaseName] = [];
        }
        var databaseDic = DistributionDictionary[databaseName];

        var con = dbConnection ?? Connection;
        ChangeDatabaseSpecial(con, databaseName);
        if (con is not null && con.State != ConnectionState.Open)
        {
            con.Open();
        }

        using var cmd = CreateCommandFromConnection(con);
        cmd.CommandText = NetezzaCatalogSql.GetDistributeSql(databaseName);

        var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            string schema = rdr.GetString(0);
            string tabName = rdr.GetString(1);

            if (!databaseDic.TryGetValue(schema, out Dictionary<string, List<string>>? databaseDictLevel1))
            {
                databaseDictLevel1 = [];
                databaseDic[schema] = databaseDictLevel1;
            }

            if (!databaseDictLevel1.TryGetValue(tabName, out List<string>? databaseDictLevel2))
            {
                databaseDictLevel2 = [];
                databaseDictLevel1[tabName] = databaseDictLevel2;
            }

            databaseDictLevel2.Add(rdr.GetString(3));
        }


        lock (_lock2)
        {
            OrganizeDictionary[databaseName] = [];
        }
        databaseDic = OrganizeDictionary[databaseName];

        using var cmd2 = CreateCommandFromConnection(con);
        cmd2.CommandText = NetezzaCatalogSql.GetOrganizeSql(databaseName);

        var rdr2 = cmd2.ExecuteReader();
        while (rdr2.Read())
        {
            string schema = rdr2.GetString(0);
            string tabName = rdr2.GetString(1);

            if (!databaseDic.TryGetValue(schema, out Dictionary<string, List<string>>? databaseDictLevel1))
            {
                databaseDictLevel1 = [];
                databaseDic[schema] = databaseDictLevel1;
            }

            if (!databaseDictLevel1.TryGetValue(tabName, out List<string>? databaseDictLevel2))
            {
                databaseDictLevel2 = [];
                databaseDictLevel1[tabName] = databaseDictLevel2;
            }

            databaseDictLevel2.Add(rdr2.GetString(3));
        }
    }

    public override async ValueTask GetCreateExternalTextStringBuilder(StringBuilder sb, string database, string schema, string tableName)
    {
        if (!_exteralTableDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.ExternalTable], database);
        }

        ExternaTableCachedInfo? cached = null;
        if (_exteralTableDictCache.TryGetValue(database, out var bySchema)
            && bySchema.TryGetValue(schema, out var byTable))
        {
            byTable.TryGetValue(tableName, out cached);
        }

        var input = NetezzaDdlInputMapper.BuildExternalInput(this, database, schema, tableName, cached);
        _ddlTextBuilder.AppendCreateExternal(sb, input);
    }

    public override string GetCheckDistributeText(string database, string schema, string tableName)
    {
        return _ddlBuilder.GetCheckDistributeText(database, schema, tableName);
    }

    public override bool IsTypeInDatabaseSupported(TypeInDatabaseEnum tpe)
    {
        return true;
    }
    public override async ValueTask GetCreateProcedureTextStringBuilder(StringBuilder sb, string database, string schema, string procName, bool forceFreshCode = false)
    {
        if (!_procedureDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.Procedure], database);
        }
        else if (forceFreshCode)
        {
            await CacheAllObjects([TypeInDatabaseEnum.Procedure], database, procName);
        }

        if (_procedureDictCache.TryGetValue(database, out var d1) && d1.TryGetValue(schema, out var d2) &&
            d2.TryGetValue(procName, out var d3))
        {
            string returns = string.IsNullOrWhiteSpace(d3.Returns) ? "VOID" : NetezzaProcTypes.FixProcedureReturnType(d3.Returns);
            var input = NetezzaDdlInputFactory.BuildProcedure(new NetezzaProcedureDefinition(
                database,
                schema,
                procName,
                returns,
                d3.ProcedureSource ?? string.Empty,
                d3.Arguments,
                d3.ExecuteAsOwner,
                d3.Desc));
            _ddlTextBuilder.AppendCreateProcedure(sb, input);
        }
    }

    public override string GetCreateProcedurePatternText()
    {
        return _ddlBuilder.GetCreateProcedurePatternText();
    }

    public string GetCreateFluidSample(string database, string schema, string tableName)
    {
        return _ddlBuilder.GetCreateFluidSample(database, schema, tableName);
    }

    public override async ValueTask GetCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null)
    {
        if (!DistributionDictionary.ContainsKey(database))
        {
            await Task.Run(() => FillDistInfoForDatabase(database));
        }

        if (!keysDictionary.ContainsKey(database))
        {
            await FillKeysInfoForDatabaseAsync(database);
        }

        TryGetTableMetadata(database, schema, tableName, out var tableComment, out var tableOwner);

        var input = NetezzaDdlInputMapper.BuildTableInput(
            this,
            database,
            schema,
            tableName,
            overrideTableName,
            middleCode,
            endingCode,
            tableComment,
            tableOwner);

        _ddlTextBuilder.AppendCreateTable(sb, input, distOverride);
    }

    public override async ValueTask GetReCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName)
    {
        if (!DistributionDictionary.ContainsKey(database))
        {
            await Task.Run(() => FillDistInfoForDatabase(database));
        }

        if (!keysDictionary.ContainsKey(database))
        {
            await FillKeysInfoForDatabaseAsync(database);
        }

        TryGetTableMetadata(database, schema, tableName, out var tableComment, out var tableOwner);

        var input = NetezzaDdlInputMapper.BuildTableInput(
            this,
            database,
            schema,
            tableName,
            tableComment: tableComment,
            tableOwner: tableOwner);

        _ddlTextBuilder.AppendRecreateTable(sb, input);
    }

    public override async ValueTask GetCreateViewTextStringBuilder(StringBuilder sb, string database, string schema, string tableName)
    {
        if (!_viewDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.View], database);
        }

        string viewSource = string.Empty;
        if (_viewDictCache.TryGetValue(database, out var bySchema)
            && bySchema.TryGetValue(schema, out var byView)
            && byView.TryGetValue(tableName, out var cachedView))
        {
            viewSource = cachedView.ViewSource;
        }

        TryGetTableMetadata(database, schema, tableName, out var viewComment, out _);

        var input = NetezzaDdlInputMapper.BuildViewInput(database, schema, tableName, viewSource, viewComment);
        _ddlTextBuilder.AppendCreateView(sb, input);
    }

    private void TryGetTableMetadata(string database, string schema, string tableName, out string? comment, out string? owner)
    {
        comment = null;
        owner = null;
        if (_databaseSchemaTable.TryGetValue(database, out var bySchema)
            && bySchema.TryGetValue(schema, out var byTable)
            && byTable.TryGetValue(tableName, out var tableItem))
        {
            comment = tableItem.Desc;
            owner = tableItem.Owner;
        }
    }

    protected virtual string DriverName => "dotnet";
    public override async Task DbSpecificImportPart(IImportJob importJob, string randName, Action<string>? progress, bool tableExists = false)
    {
        try
        {
            using var conn = GetConnection(Connection.Database, pooling: false);
            if (conn is not null)
            {
                await Task.Run(() => conn.Open());
                await NetezzaImportHelper.NetezzaImportExecute(conn, TempDataDirectory, importJob, randName, progress, DriverName);
                var t = Task.Run(() => conn.Close());
                Task.WaitAny(t, Task.Delay(2_000));
                _ = t.ContinueWith(static x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex)
        {
            progress?.Invoke($"[ERROR] {ex.Message}");
            randName = ex.Message;
        }
    }

    private readonly Lock _lockForExternales = new();
    public void ReadExternalTable(string database, DbDataReader rdr)
    {
        while (rdr.Read())
        {
            string? schema = rdr.GetValue(0) as string;
            string extTableName = rdr.GetString(1);
            var cached = NetezzaExternalOptionsMapper.FromLegacyReader(rdr);

            lock (_lockForExternales)
            {
                ref var databaseItem = ref CollectionsMarshal.GetValueRefOrAddDefault(_exteralTableDictCache, database, out var _);
                databaseItem ??= [];
                ref var schemaItem = ref CollectionsMarshal.GetValueRefOrAddDefault(databaseItem!, schema!, out var _);
                schemaItem ??= [];
                schemaItem[extTableName] = cached;
            }
        }
    }

    public string GetExternalDataObject(string database, string schema, string itemNameOrSignature)
    {
        if (_exteralTableDictCache.TryGetValue(database, out var t1) &&
            schema is not null && t1.TryGetValue(schema, out var t2) &&
            itemNameOrSignature is not null && t2.TryGetValue(itemNameOrSignature, out var finalItem))
        {
            return finalItem.DataObject ?? string.Empty;
        }
        return string.Empty;
    }


}
