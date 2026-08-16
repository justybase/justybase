using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using Microsoft.Data.Sqlite;
using JustyBase.SqliteDriver.Metadata;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Collections.Concurrent;

namespace JustyBase.SqliteDriver;

public sealed class Sqlite : DatabaseService, ILoginDataAwareDatabaseService, IDatabaseConnectionConfigurator
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.Sqlite;

    private readonly Lock _memoryConnectionLock = new();
    private SqliteConnection? _memoryConnection;
    private string? _memoryDataSource;
    private string? _anonymousMemoryDataSource;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SqliteSchemaSnapshot>> _sqliteSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Database, string Schema, string Table), DatabaseColumn[]> _sqliteColumns = new();

    public SqliteConnectionOptions ConnectionOptions { get; set; } = new();

    public Sqlite(string username, string password, string port, string ip, string db, int connectionTimeout)
        : base(username, password, port, ip, db, connectionTimeout)
    {
        DatabaseType = WHO_I_AM_CONST;
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.DatabaseSchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional;
        preferDatabaseInCodes = false;
        PrefrerUpperCase = false;
    }

    public void ApplyLoginData(LoginDataModel loginData)
    {
        ArgumentNullException.ThrowIfNull(loginData);
        ConnectionOptions = loginData.SqliteOptions ?? new SqliteConnectionOptions();
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        string dataSource = ResolveDataSource(Ip, Database, databaseName);
        bool isMemory = IsMemoryDataSource(dataSource);

        if (isMemory)
        {
            lock (_memoryConnectionLock)
            {
                string sharedDataSource = ResolveSharedMemoryDataSource(dataSource);
                if (_memoryConnection is null
                    || _memoryConnection.State != ConnectionState.Open
                    || !string.Equals(_memoryDataSource, sharedDataSource, StringComparison.OrdinalIgnoreCase))
                {
                    _memoryConnection?.Dispose();
                    _memoryDataSource = sharedDataSource;
                    _memoryConnection = CreateConnection(sharedDataSource, isMemory: true, pooling: false);
                    _memoryConnection.Open();
                    ConfigureOpenConnection(_memoryConnection);
                    Connection = _memoryConnection;
                }

                // Keep the anchor open so disposing this caller-owned connection does not
                // destroy the in-memory database.
                return CreateConnection(sharedDataSource, isMemory: true, pooling);
            }
        }

        SqliteConnection connection = CreateConnection(dataSource, isMemory: false, pooling);
        Connection = connection;
        return connection;
    }

    public void ConfigureOpenConnection(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConnection || sqliteConnection.State != ConnectionState.Open)
        {
            return;
        }

        using (var foreignKeys = sqliteConnection.CreateCommand())
        {
            foreignKeys.CommandText = $"PRAGMA foreign_keys = {(ConnectionOptions.ForeignKeys ? "ON" : "OFF")};";
            foreignKeys.ExecuteNonQuery();
        }

        using (var busyTimeout = sqliteConnection.CreateCommand())
        {
            busyTimeout.CommandText = $"PRAGMA busy_timeout = {Math.Max(ConnectionOptions.BusyTimeoutMilliseconds, 1)};";
            busyTimeout.ExecuteNonQuery();
        }

        var attachedCatalogs = ReadCatalogs(sqliteConnection)
            .Select(catalog => catalog.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (SqliteAttachedDatabaseOptions attached in ConnectionOptions.AttachedDatabases)
        {
            if (string.IsNullOrWhiteSpace(attached.Alias) || string.IsNullOrWhiteSpace(attached.FilePath))
            {
                continue;
            }

            if (!attachedCatalogs.Add(attached.Alias))
            {
                continue;
            }

            using var attach = sqliteConnection.CreateCommand();
            string path = attached.FilePath;
            if (attached.ReadOnly)
            {
                string uri = ToSqliteFileUri(path);
                path = uri.Contains('?', StringComparison.Ordinal)
                    ? $"{uri}&mode=ro"
                    : $"{uri}?mode=ro";
            }
            attach.CommandText = $"ATTACH DATABASE '{EscapeSqlLiteral(path)}' AS {QuoteIdentifier(attached.Alias)};";
            attach.ExecuteNonQuery();
        }
    }

    private SqliteConnection CreateConnection(string dataSource, bool isMemory, bool pooling)
    {
        string connectionDataSource = BuildConnectionDataSource(dataSource, isMemory);
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = connectionDataSource,
            Mode = isMemory
                ? SqliteOpenMode.Memory
                : ConnectionOptions.ReadOnly || ConnectionOptions.Immutable
                    ? SqliteOpenMode.ReadOnly
                    : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = pooling,
            ForeignKeys = ConnectionOptions.ForeignKeys,
            DefaultTimeout = Math.Max(
                1,
                (int)Math.Ceiling(Math.Max(ConnectionOptions.BusyTimeoutMilliseconds, 1) / 1_000d)),
        };

        return new SqliteConnection(connectionString.ConnectionString);
    }

    private string BuildConnectionDataSource(string dataSource, bool isMemory)
    {
        bool uriRequired = ConnectionOptions.UseUri
            || ConnectionOptions.Immutable
            || ConnectionOptions.AttachedDatabases.Any(attached => attached.ReadOnly);
        if (isMemory || !uriRequired)
        {
            return dataSource;
        }

        string uri = dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? dataSource
            : ToSqliteFileUri(dataSource);
        if (!ConnectionOptions.Immutable)
        {
            return uri;
        }

        return uri.Contains('?', StringComparison.Ordinal)
            ? $"{uri}&immutable=1"
            : $"{uri}?immutable=1";
    }

    private static string ToSqliteFileUri(string path)
        => path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? path
            : new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private string ResolveSharedMemoryDataSource(string dataSource)
    {
        if (!dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return dataSource;
        }

        return _anonymousMemoryDataSource ??= $"justybase-{Guid.NewGuid():N}";
    }

    protected override string GetSqlTablesAndOtherObjects(string dbName)
    {
        const string schema = "main";
        return $"""
            SELECT
                CAST(m.rowid AS INTEGER)
                , m.name
                , NULL AS DESC_
                , '{EscapeSqlLiteral(schema)}' AS SCHEMA_
                , UPPER(m.type)
                , '' AS OWNER
                , NULL
            FROM {QuoteIdentifier(schema)}.sqlite_schema AS m
            WHERE m.name NOT LIKE 'sqlite_%'
            ORDER BY m.rowid
            """;
    }

    protected override string? GetProceduresSql(string database, string objectFilterName) => null;

    protected override string? GetViewsSql(string database, string objectFilterName)
    {
        const string schema = "main";
        return $"""
            SELECT
                '{EscapeSqlLiteral(schema)}' AS SCHEMA_
                , m.name
                , m.sql
            FROM {QuoteIdentifier(schema)}.sqlite_schema AS m
            WHERE m.name NOT LIKE 'sqlite_%'
              AND m.type = 'view'
            ORDER BY m.name
            """;
    }

    protected override List<(string databaseName, string defaultSchema)> GetDatabases()
    {
        string dataSource = ResolveDataSource(Ip, Database, null);
        string databaseName = string.IsNullOrWhiteSpace(Database) ? dataSource : Database;
        return [(databaseName, "main")];
    }

    protected override void LoadDatabaseObject(string database, DbConnection con)
    {
        ConfigureOpenConnection(con);
        var schemaCache = _sqliteSnapshots.GetOrAdd(database, static _ => new(StringComparer.OrdinalIgnoreCase));
        var databaseCache = _databaseSchemaTable[database];

        foreach (SqliteCatalogInfo catalog in ReadCatalogs(con))
        {
            var indexDefinitions = ReadIndexes(con, catalog.Name);
            var foreignKeys = new Dictionary<string, IReadOnlyList<SqliteForeignKeyDefinition>>(StringComparer.OrdinalIgnoreCase);
            var tables = ReadTableInfo(con, catalog.Name, foreignKeys);

            var snapshot = new SqliteSchemaSnapshot(
                catalog.Name,
                tables,
                indexDefinitions,
                foreignKeys,
                DateTimeOffset.UtcNow);
            schemaCache[catalog.Name] = snapshot;

            if (!databaseCache.TryGetValue(catalog.Name, out var objects))
            {
                objects = new Dictionary<string, DatabaseObject>(StringComparer.OrdinalIgnoreCase);
                databaseCache[catalog.Name] = objects;
            }

            int rowId = 0;
            using var command = con.CreateCommand();
            command.CommandText = $"SELECT rowid, name, tbl_name, type, sql FROM {QuoteIdentifier(catalog.Name)}.sqlite_schema WHERE name NOT LIKE 'sqlite_%' AND type IN ('table','view','index','trigger') ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rowId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                string name = reader.GetString(1);
                string parentName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string textType = reader.GetString(3).ToUpperInvariant();
                string? definition = reader.IsDBNull(4) ? null : reader.GetString(4);
                TypeInDatabaseEnum type = textType.GetTypeInDatabaseEnumFromDbName();

                if (type == TypeInDatabaseEnum.Table && tables.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Type == "shadow")
                {
                    continue;
                }

                string? description = type switch
                {
                    TypeInDatabaseEnum.Index => BuildIndexDescription(indexDefinitions.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))),
                    TypeInDatabaseEnum.Trigger when !string.IsNullOrWhiteSpace(parentName) => $"SQLite trigger on {parentName}",
                    _ => null
                };

                objects[name] = new DatabaseObject(rowId, name, description, type, textType, "", null)
                {
                    ParentObjectName = type is TypeInDatabaseEnum.Index or TypeInDatabaseEnum.Trigger ? parentName : null,
                    DefinitionSql = definition,
                    IsSystemObject = name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)
                };
            }
        }
    }

    protected override void LoadColumns(string database, DbConnection con)
    {
        foreach (SqliteCatalogInfo catalog in ReadCatalogs(con))
        {
            if (!_databaseSchemaTable.TryGetValue(database, out var schemas)
                || !schemas.TryGetValue(catalog.Name, out var objects))
            {
                continue;
            }

            foreach (DatabaseObject table in objects.Values.Where(x => x.TypeInDatabase is TypeInDatabaseEnum.Table or TypeInDatabaseEnum.View))
            {
                _sqliteColumns[(database, catalog.Name, table.Name)] = ReadColumns(con, catalog.Name, table.Name);
            }
        }
    }

    public override IEnumerable<DatabaseColumn> GetColumns(string? database, string? schema, string? table, string filter)
    {
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table))
        {
            yield break;
        }

        if (_sqliteColumns.TryGetValue((database, schema, table), out var columns))
        {
            foreach (DatabaseColumn column in columns)
            {
                if (DatabaseFilterHelper.MatchesContains(column.Name, filter))
                {
                    yield return column;
                }
            }
        }
    }

    public override void ClearCachedData()
    {
        base.ClearCachedData();
        _sqliteSnapshots.Clear();
        _sqliteColumns.Clear();
    }

    public SqliteSchemaSnapshot GetSchemaSnapshot(string database, string schema)
        => _sqliteSnapshots.TryGetValue(database, out var schemas)
            && schemas.TryGetValue(schema, out var snapshot)
            ? snapshot
            : SqliteSchemaSnapshot.Empty(schema);

    protected override string GetSqlOfColumns(string dbName)
    {
        const string schema = "main";
        return $"""
            SELECT
                CAST(m.rowid AS INTEGER)
                , p.name
                , NULL AS DESC_
                , CASE
                    WHEN COALESCE(p.type, '') = '' THEN 'TEXT'
                    ELSE p.type
                  END || CASE WHEN p."notnull" <> 0 THEN ' NOT NULL' ELSE '' END AS FORMAT_TYPE
                , p."notnull"
                , p.dflt_value
            FROM {QuoteIdentifier(schema)}.sqlite_schema AS m
            JOIN pragma_table_info(m.name) AS p
            WHERE m.name NOT LIKE 'sqlite_%'
              AND m.type IN ('table', 'view')
            ORDER BY m.rowid, p.cid
            """;
    }

    protected override string? GetExternalTableSql(string database) => null;

    protected override string? GetSynonymSql(string database) => null;

    public override bool IsTypeInDatabaseSupported(TypeInDatabaseEnum typeInDatabase)
        => typeInDatabase is not TypeInDatabaseEnum.Procedure
            and not TypeInDatabaseEnum.Function
            and not TypeInDatabaseEnum.Sequence
            and not TypeInDatabaseEnum.ExternalTable
            and not TypeInDatabaseEnum.Synonym;

    public override async Task DbSpecificImportPart(
        IImportJob importJob,
        string randName,
        Action<string>? progress,
        bool tableExists = false)
    {
        ArgumentNullException.ThrowIfNull(importJob);

        await Task.Run(() => ImportRows(importJob, randName, progress, tableExists)).ConfigureAwait(false);
    }

    public override string GetTableDropCode(string fullName) => $"DROP TABLE IF EXISTS {fullName};";

    public override string GetCreateFromCode(string fullName)
        => $"CREATE TABLE ABC AS SELECT T1.* FROM {fullName} AS T1;";

    public override string GetDrop(string table, string database, string schema)
    {
        string tableName = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"DROP TABLE IF EXISTS {tableName};";
    }

    public override string GetEmpty(string table, string database, string schema)
    {
        string tableName = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"DELETE FROM {tableName};";
    }

    public override string GetGenerateStats(string database, string schema, string table)
        => $"ANALYZE {GetQuotedTwoOrTreePartName(database, schema, table)};";

    public override string GetOrganize(string database, string schema, string table)
        => $"ANALYZE {GetQuotedTwoOrTreePartName(database, schema, table)};";

    public override string GetGroom(string database, string schema, string table) => "VACUUM;";

    public string GetIntegrityCheckSql(string? schema = null)
        => $"PRAGMA {QuoteIdentifier(GetSchemaName(schema))}.integrity_check;";

    public string GetForeignKeyCheckSql(string? schema = null)
        => $"PRAGMA {QuoteIdentifier(GetSchemaName(schema))}.foreign_key_check;";

    public string GetDatabaseInfoSql()
        => """
            PRAGMA database_list;
            PRAGMA page_size;
            PRAGMA page_count;
            PRAGMA freelist_count;
            PRAGMA journal_mode;
            PRAGMA synchronous;
            PRAGMA foreign_keys;
            PRAGMA user_version;
            """;

    public override string GetCreateIndexPatternText(string database, string schema, string tableName)
    {
        string table = GetQuotedTwoOrTreePartName(database, schema, tableName);
        string indexName = QuoteNameIfNeeded($"IX_{tableName}");
        return $"CREATE INDEX IF NOT EXISTS {indexName} ON {table} (<COL1>);";
    }

    public override string GetKeyCodeText(string database, string schema, string tableName)
        => "-- SQLite primary keys must be declared in CREATE TABLE; ALTER TABLE cannot add one.";

    public override string GetKeyUniqueCodeText(string database, string schema, string tableName)
    {
        string table = GetQuotedTwoOrTreePartName(database, schema, tableName);
        string indexName = QuoteNameIfNeeded($"UK_{tableName}");
        return $"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {table} (<COL1>,<COL2>);";
    }

    public override string GetCreateProcedurePatternText()
        => "-- SQLite has no stored procedures; use a view or trigger.";

    public override async ValueTask GetCreateTableTextStringBuilder(
        StringBuilder sb,
        string database,
        string schema,
        string tableName,
        string? overrideTableName = null,
        string? middleCode = null,
        string? endingCode = null,
        List<string>? distOverride = null)
    {
        await AppendSchemaSql(sb, database, schema, tableName, "table", overrideTableName).ConfigureAwait(false);
    }

    public override async ValueTask GetCreateViewTextStringBuilder(
        StringBuilder stringBuilder,
        string database,
        string schema,
        string tableName)
    {
        await AppendSchemaSql(stringBuilder, database, schema, tableName, "view", null).ConfigureAwait(false);
    }

    public override async ValueTask GetCreateIndexTextStringBuilder(
        StringBuilder stringBuilder,
        string database,
        string schema,
        string indexName)
    {
        await AppendSchemaSql(stringBuilder, database, schema, indexName, "index", null).ConfigureAwait(false);
    }

    public override async ValueTask GetCreateTriggerTextStringBuilder(
        StringBuilder stringBuilder,
        string database,
        string schema,
        string triggerName)
    {
        await AppendSchemaSql(stringBuilder, database, schema, triggerName, "trigger", null).ConfigureAwait(false);
    }

    private void ImportRows(IImportJob importJob, string tableName, Action<string>? progress, bool tableExists)
    {
        using DbConnection connection = GetConnection(null, pooling: false);
        connection.Open();
        ConfigureOpenConnection(connection);
        using DbTransaction transaction = connection.BeginTransaction();

        IReadOnlyList<string> headers = importJob.ColumnHeadersNames;
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("SQLite import requires at least one column.");
        }

        string quotedTable = QuoteQualifiedIdentifier(tableName);
        if (!tableExists)
        {
            string[] definitions = importJob.ReturnHeadersWithDataTypes(DatabaseKind.Sqlite);
            string[] columnDefinitions = definitions
                .Select((definition, index) => FormatColumnDefinition(definition, headers[index]))
                .ToArray();

            using DbCommand create = connection.CreateCommand();
            create.Transaction = transaction;
            create.CommandText = $"CREATE TABLE {quotedTable} ({string.Join(",", columnDefinitions)});";
            create.ExecuteNonQuery();
        }

        string parameterList = string.Join(",", Enumerable.Range(0, headers.Count).Select(i => $"@p{i}"));
        string columnList = string.Join(",", headers.Select(QuoteIdentifier));
        string insertTarget = tableExists
            ? quotedTable
            : $"{quotedTable} ({columnList})";

        using DbCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {insertTarget} VALUES ({parameterList});";
        for (int i = 0; i < headers.Count; i++)
        {
            DbParameter parameter = insert.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            insert.Parameters.Add(parameter);
        }

        insert.Prepare();
        object[] values = new object[headers.Count];
        long rowCount = 0;
        try
        {
            while (importJob.AsReader.Read())
            {
                importJob.AsReader.GetValues(values);
                for (int i = 0; i < values.Length; i++)
                {
                    insert.Parameters[i].Value = NormalizeParameterValue(values[i]);
                }

                insert.ExecuteNonQuery();
                rowCount++;
                if (rowCount % 1_000 == 0)
                {
                    progress?.Invoke($"Copied {rowCount:N0}");
                }
            }

            transaction.Commit();
            progress?.Invoke($"Copied {rowCount:N0}");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async ValueTask AppendSchemaSql(
        StringBuilder output,
        string database,
        string schema,
        string objectName,
        string objectType,
        string? overrideName)
    {
        await Task.Run(() =>
        {
            using DbConnection connection = GetConnection(database, pooling: false);
            connection.Open();
            ConfigureOpenConnection(connection);
            using DbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT sql FROM {QuoteIdentifier(GetSchemaName(schema))}.sqlite_schema WHERE type = @type AND name = @name LIMIT 1;";

            DbParameter typeParameter = command.CreateParameter();
            typeParameter.ParameterName = "@type";
            typeParameter.Value = objectType;
            command.Parameters.Add(typeParameter);

            DbParameter nameParameter = command.CreateParameter();
            nameParameter.ParameterName = "@name";
            nameParameter.Value = objectName;
            command.Parameters.Add(nameParameter);

            string? sql = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(sql))
            {
                output.Append("-- SQLite definition not found for ").Append(objectName).AppendLine();
                return;
            }

            if (!string.IsNullOrWhiteSpace(overrideName))
            {
                sql = ReplaceObjectName(sql, objectName, overrideName);
            }

            output.Append(sql.TrimEnd());
            if (!sql.TrimEnd().EndsWith(';'))
            {
                output.Append(';');
            }

            output.AppendLine();
        }).ConfigureAwait(false);
    }

    private static IReadOnlyList<SqliteCatalogInfo> ReadCatalogs(DbConnection connection)
    {
        var catalogs = new List<SqliteCatalogInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA database_list;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            catalogs.Add(new SqliteCatalogInfo(
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)));
        }

        return catalogs;
    }

    private static IReadOnlyList<SqliteTableInfo> ReadTableInfo(
        DbConnection connection,
        string catalog,
        Dictionary<string, IReadOnlyList<SqliteForeignKeyDefinition>> foreignKeys)
    {
        var tableTypes = new Dictionary<string, (string Type, int Ncol, bool WithoutRowId, bool Strict)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var tableList = connection.CreateCommand();
            tableList.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.table_list;";
            using var tableReader = tableList.ExecuteReader();
            while (tableReader.Read())
            {
                string name = tableReader.GetString(1);
                string type = tableReader.GetString(2);
                int ncol = Convert.ToInt32(tableReader.GetValue(3), CultureInfo.InvariantCulture);
                bool withoutRowId = Convert.ToInt32(tableReader.GetValue(4), CultureInfo.InvariantCulture) != 0;
                bool strict = tableReader.FieldCount > 5 && !tableReader.IsDBNull(5)
                    && Convert.ToInt32(tableReader.GetValue(5), CultureInfo.InvariantCulture) != 0;
                tableTypes[name] = (type, ncol, withoutRowId, strict);
            }
        }
        catch (DbException)
        {
            // table_list is newer than sqlite_schema. The catalog query below
            // remains a valid compatibility fallback.
        }

        var result = new List<SqliteTableInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name, type, sql FROM {QuoteIdentifier(catalog)}.sqlite_schema WHERE name NOT LIKE 'sqlite_%' AND type IN ('table','view') ORDER BY name;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string name = reader.GetString(0);
            string type = reader.GetString(1);
            string? sql = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (tableTypes.TryGetValue(name, out var tableInfo) && tableInfo.Type.Equals("shadow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int columnCount = tableTypes.TryGetValue(name, out tableInfo) ? tableInfo.Ncol : 0;
            bool withoutRowId = tableTypes.TryGetValue(name, out tableInfo) && tableInfo.WithoutRowId;
            bool strict = tableTypes.TryGetValue(name, out tableInfo) && tableInfo.Strict;
            string objectType = tableTypes.TryGetValue(name, out tableInfo) ? tableInfo.Type : type;
            result.Add(new SqliteTableInfo(
                catalog,
                name,
                objectType,
                columnCount,
                withoutRowId,
                strict,
                ExtractVirtualTableModule(sql),
                sql));
            foreignKeys[name] = ReadForeignKeys(connection, catalog, name);
        }

        return result;
    }

    private static IReadOnlyList<SqliteForeignKeyDefinition> ReadForeignKeys(DbConnection connection, string catalog, string table)
    {
        var result = new List<SqliteForeignKeyDefinition>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.foreign_key_list({QuoteIdentifier(table)});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new SqliteForeignKeyDefinition(
                    table,
                    Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }
        catch (DbException)
        {
            // A malformed/legacy catalog must not prevent the rest of the tree
            // from loading.
        }

        return result;
    }

    private static IReadOnlyList<SqliteIndexDefinition> ReadIndexes(DbConnection connection, string catalog)
    {
        var definitions = new List<SqliteIndexDefinition>();
        using var catalogCommand = connection.CreateCommand();
        catalogCommand.CommandText = $"SELECT name, tbl_name, sql FROM {QuoteIdentifier(catalog)}.sqlite_schema WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var catalogReader = catalogCommand.ExecuteReader();
        var indexRows = new List<(string Name, string Table, string? Sql)>();
        while (catalogReader.Read())
        {
            indexRows.Add((catalogReader.GetString(0), catalogReader.GetString(1), catalogReader.IsDBNull(2) ? null : catalogReader.GetString(2)));
        }

        foreach (var row in indexRows)
        {
            bool unique = false;
            bool partial = false;
            string origin = "c";
            try
            {
                using var listCommand = connection.CreateCommand();
                listCommand.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.index_list({QuoteIdentifier(row.Table)});";
                using var listReader = listCommand.ExecuteReader();
                while (listReader.Read())
                {
                    if (!listReader.GetString(1).Equals(row.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    unique = Convert.ToInt32(listReader.GetValue(2), CultureInfo.InvariantCulture) != 0;
                    origin = listReader.FieldCount > 3 && !listReader.IsDBNull(3) ? listReader.GetString(3) : "c";
                    partial = listReader.FieldCount > 4 && !listReader.IsDBNull(4)
                        && Convert.ToInt32(listReader.GetValue(4), CultureInfo.InvariantCulture) != 0;
                    break;
                }
            }
            catch (DbException)
            {
                unique = row.Sql?.Contains("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase) == true;
            }

            var columns = new List<SqliteIndexColumn>();
            try
            {
                using var infoCommand = connection.CreateCommand();
                infoCommand.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.index_xinfo({QuoteIdentifier(row.Name)});";
                using var infoReader = infoCommand.ExecuteReader();
                while (infoReader.Read())
                {
                    int sequence = Convert.ToInt32(infoReader.GetValue(0), CultureInfo.InvariantCulture);
                    int tableColumn = Convert.ToInt32(infoReader.GetValue(1), CultureInfo.InvariantCulture);
                    string? name = infoReader.IsDBNull(2) ? null : infoReader.GetString(2);
                    bool descending = infoReader.FieldCount > 3 && !infoReader.IsDBNull(3)
                        && Convert.ToInt32(infoReader.GetValue(3), CultureInfo.InvariantCulture) != 0;
                    string? collation = infoReader.FieldCount > 4 && !infoReader.IsDBNull(4) ? infoReader.GetString(4) : null;
                    bool isKey = infoReader.FieldCount <= 5 || infoReader.IsDBNull(5)
                        || Convert.ToInt32(infoReader.GetValue(5), CultureInfo.InvariantCulture) != 0;
                    columns.Add(new SqliteIndexColumn(sequence, tableColumn, name, descending, collation, isKey));
                }
            }
            catch (DbException)
            {
                using var infoCommand = connection.CreateCommand();
                infoCommand.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.index_info({QuoteIdentifier(row.Name)});";
                using var infoReader = infoCommand.ExecuteReader();
                while (infoReader.Read())
                {
                    columns.Add(new SqliteIndexColumn(
                        Convert.ToInt32(infoReader.GetValue(0), CultureInfo.InvariantCulture),
                        Convert.ToInt32(infoReader.GetValue(1), CultureInfo.InvariantCulture),
                        infoReader.IsDBNull(2) ? null : infoReader.GetString(2),
                        false,
                        null,
                        true));
                }
            }

            definitions.Add(new SqliteIndexDefinition(catalog, row.Name, row.Table, unique, partial, origin, row.Sql, columns));
        }

        return definitions;
    }

    private static DatabaseColumn[] ReadColumns(DbConnection connection, string catalog, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.table_xinfo({QuoteIdentifier(table)});";
        try
        {
            using var reader = command.ExecuteReader();
            return ReadColumns(reader, includeHiddenFlag: true);
        }
        catch (DbException)
        {
            command.CommandText = $"PRAGMA {QuoteIdentifier(catalog)}.table_info({QuoteIdentifier(table)});";
            using var reader = command.ExecuteReader();
            return ReadColumns(reader, includeHiddenFlag: false);
        }
    }

    private static DatabaseColumn[] ReadColumns(DbDataReader reader, bool includeHiddenFlag)
    {
        var columns = new List<DatabaseColumn>();
        while (reader.Read())
        {
            int hidden = includeHiddenFlag && reader.FieldCount > 6 && !reader.IsDBNull(6)
                ? Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture)
                : 0;
            int pk = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture);
            string declaredType = reader.IsDBNull(2) || string.IsNullOrWhiteSpace(reader.GetString(2)) ? "TEXT" : reader.GetString(2);
            bool notNull = !reader.IsDBNull(3) && Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture) != 0;
            string? defaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();
            columns.Add(new DatabaseColumn(
                reader.GetString(1),
                null,
                declaredType + (notNull ? " NOT NULL" : string.Empty),
                notNull,
                defaultValue)
            {
                IsPrimaryKey = pk > 0,
                PrimaryKeyOrdinal = pk,
                IsGenerated = hidden is 2 or 3,
                IsHidden = hidden == 1
            });
        }

        return columns.ToArray();
    }

    private static string? ExtractVirtualTableModule(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)
            || !sql.Contains("VIRTUAL TABLE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int usingIndex = sql.IndexOf(" USING ", StringComparison.OrdinalIgnoreCase);
        if (usingIndex < 0)
        {
            return null;
        }

        string module = sql[(usingIndex + " USING ".Length)..].Trim();
        int argumentStart = module.IndexOf('(');
        return (argumentStart >= 0 ? module[..argumentStart] : module)
            .Trim()
            .TrimEnd(';');
    }

    private static string? BuildIndexDescription(SqliteIndexDefinition? index)
    {
        if (index is null)
        {
            return null;
        }

        string flags = string.Join(", ", new[]
        {
            index.IsUnique ? "unique" : null,
            index.IsPartial ? "partial" : null,
            $"origin={index.Origin}"
        }.Where(x => x is not null));
        string columns = string.Join(", ", index.Columns.Where(x => x.IsKey).Select(x => x.Name ?? "<expression>"));
        return $"SQLite index on {index.TableName} ({columns}){(flags.Length == 0 ? string.Empty : $" [{flags}]")}";
    }

    internal static string ResolveDataSource(string ip, string database, string? databaseName)
    {
        string candidate = string.IsNullOrWhiteSpace(databaseName) ? database : databaseName;
        if (string.IsNullOrWhiteSpace(candidate) && IsMemoryDataSource(ip))
        {
            candidate = ip;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return ":memory:";
        }

        if (IsMemoryDataSource(candidate) || candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (Path.IsPathRooted(candidate) || string.IsNullOrWhiteSpace(ip))
        {
            return candidate;
        }

        return Path.Combine(ip, candidate);
    }

    internal static bool IsMemoryDataSource(string? dataSource)
        => !string.IsNullOrWhiteSpace(dataSource)
            && (dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                || dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase)
                || dataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase));

    private static string GetSchemaName(string? schema)
        => string.IsNullOrWhiteSpace(schema) ? "main" : schema;

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string QuoteQualifiedIdentifier(string identifier)
        => string.Join('.', identifier.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(QuoteIdentifier));

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FormatColumnDefinition(string definition, string columnName)
    {
        if (definition.StartsWith(columnName, StringComparison.Ordinal))
        {
            return $"{QuoteIdentifier(columnName)}{definition[columnName.Length..]}";
        }

        return $"{QuoteIdentifier(columnName)} TEXT";
    }

    private static object NormalizeParameterValue(object? value)
        => value switch
        {
            null => DBNull.Value,
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            _ => value,
        };

    private static string ReplaceObjectName(string sql, string originalName, string replacementName)
    {
        string quotedOriginal = QuoteIdentifier(originalName);
        string quotedReplacement = QuoteIdentifier(replacementName);
        if (sql.Contains(quotedOriginal, StringComparison.OrdinalIgnoreCase))
        {
            return sql.Replace(quotedOriginal, quotedReplacement, StringComparison.OrdinalIgnoreCase);
        }

        return sql.Replace(originalName, replacementName, StringComparison.OrdinalIgnoreCase);
    }
}
