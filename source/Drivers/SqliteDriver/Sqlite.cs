using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace JustyBase.SqliteDriver;

public sealed class Sqlite : DatabaseService
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.Sqlite;

    private readonly Lock _memoryConnectionLock = new();
    private SqliteConnection? _memoryConnection;
    private string? _memoryDataSource;
    private string? _anonymousMemoryDataSource;

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

    private SqliteConnection CreateConnection(string dataSource, bool isMemory, bool pooling)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = dataSource,
            Mode = isMemory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = pooling,
            ForeignKeys = true,
            DefaultTimeout = Math.Max(CONNECTION_TIMEOUT, 1),
        };

        return new SqliteConnection(connectionString.ConnectionString);
    }

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
        if (IsMemoryDataSource(dataSource))
        {
            // The base cache manager opens a second connection after this method.
            // A file-backed database is therefore required for persistent schema caching.
            return [];
        }

        string databaseName = string.IsNullOrWhiteSpace(Database) ? dataSource : Database;
        return [(databaseName, "main")];
    }

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

    private void ImportRows(IImportJob importJob, string tableName, Action<string>? progress, bool tableExists)
    {
        using DbConnection connection = GetConnection(null, pooling: false);
        connection.Open();
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
