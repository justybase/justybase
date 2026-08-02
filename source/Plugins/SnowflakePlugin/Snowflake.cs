using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using Snowflake.Data.Client;
using System.Data.Common;
using System.Text;

namespace SnowflakePlugin;

public sealed class Snowflake : DatabaseService, ILoginDataAwareDatabaseService
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.Snowflake;

    private const string DefaultSchemaName = "PUBLIC";
    private const string SnowflakeHostSuffix = ".snowflakecomputing.com";
    private string? _schemaName;
    private string? _warehouseName;
    private string? _roleName;

    public Snowflake(string username, string password, string port, string ip, string db, int connectionTimeout) : base(username, password, port, ip, db, connectionTimeout)
    {
        DatabaseType = WHO_I_AM_CONST;
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.DatabaseSchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional
            | CurrentAutoCompletDatabaseMode.MakeUpperCase;
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        string normalizedServer = NormalizeServer(Ip);
        string accountIdentifier = ResolveAccountIdentifier(normalizedServer);

        var builder = new SnowflakeDbConnectionStringBuilder
        {
            ["account"] = accountIdentifier,
            ["user"] = Username,
            ["password"] = Password,
            ["connection_timeout"] = CONNECTION_TIMEOUT.ToString(),
            ["pooling"] = pooling ? "true" : "false",
        };

        if (!string.IsNullOrWhiteSpace(normalizedServer) && normalizedServer.Contains('.', StringComparison.Ordinal))
        {
            builder["host"] = normalizedServer;
        }
        if (!string.IsNullOrWhiteSpace(_warehouseName))
        {
            builder["warehouse"] = _warehouseName;
        }
        if (!string.IsNullOrWhiteSpace(_roleName))
        {
            builder["role"] = _roleName;
        }

        return new SnowflakeDbConnection(builder.ConnectionString);
    }

    public void ApplyLoginData(LoginDataModel loginData)
    {
        ArgumentNullException.ThrowIfNull(loginData);

        bool useLiveTestFallback = ShouldUseLiveTestFallback(loginData);
        _schemaName = ResolveSetting(loginData.Schema, "SNOWFLAKE_LIVE_TEST_SCHEMA", useLiveTestFallback, DefaultSchemaName);
        _warehouseName = ResolveSetting(loginData.Warehouse, "SNOWFLAKE_LIVE_TEST_WAREHOUSE", useLiveTestFallback);
        _roleName = ResolveSetting(loginData.Role, "SNOWFLAKE_LIVE_TEST_ROLE", useLiveTestFallback);
    }

    protected override string GetSqlTablesAndOtherObjects(string dbName)
    {
        string tablesView = GetInformationSchemaObjectName(dbName, "TABLES");
        string proceduresView = GetInformationSchemaObjectName(dbName, "PROCEDURES");
        string functionsView = GetInformationSchemaObjectName(dbName, "FUNCTIONS");
        string sequencesView = GetInformationSchemaObjectName(dbName, "SEQUENCES");
        return
            $"""
            WITH table_objects AS
            (
                SELECT
                    DENSE_RANK() OVER
                    (
                        ORDER BY
                            TABLE_SCHEMA,
                            CASE
                                WHEN TABLE_TYPE IN ('VIEW', 'MATERIALIZED VIEW') THEN 'VIEW'
                                WHEN TABLE_TYPE = 'EXTERNAL TABLE' THEN 'EXTERNAL TABLE'
                                ELSE 'TABLE'
                            END,
                            TABLE_NAME
                    )::INT AS OBJECT_ID
                    , TABLE_NAME AS OBJECT_NAME
                    , COMMENT AS DESCRIPTION
                    , TABLE_SCHEMA
                    , CASE
                        WHEN TABLE_TYPE IN ('VIEW', 'MATERIALIZED VIEW') THEN 'VIEW'
                        WHEN TABLE_TYPE = 'EXTERNAL TABLE' THEN 'EXTERNAL TABLE'
                        ELSE 'TABLE'
                      END AS OBJECT_TYPE
                    , COALESCE(TABLE_OWNER, '') AS OWNER
                    , CREATED AS CREATEDATATIME
                FROM {tablesView}
                WHERE TABLE_SCHEMA <> 'INFORMATION_SCHEMA'
            )
            , procedure_objects AS
            (
                SELECT
                    (-1000000 - DENSE_RANK() OVER
                    (
                        ORDER BY PROCEDURE_SCHEMA, PROCEDURE_NAME, COALESCE(ARGUMENT_SIGNATURE, '')
                    ))::INT AS OBJECT_ID
                    , PROCEDURE_NAME || COALESCE(ARGUMENT_SIGNATURE, '()') AS OBJECT_NAME
                    , COMMENT AS DESCRIPTION
                    , PROCEDURE_SCHEMA AS TABLE_SCHEMA
                    , 'PROCEDURE' AS OBJECT_TYPE
                    , COALESCE(PROCEDURE_OWNER, '') AS OWNER
                    , CREATED AS CREATEDATATIME
                FROM {proceduresView}
                WHERE PROCEDURE_SCHEMA <> 'INFORMATION_SCHEMA'
            )
            , function_objects AS
            (
                SELECT
                    (-2000000 - DENSE_RANK() OVER
                    (
                        ORDER BY FUNCTION_SCHEMA, FUNCTION_NAME, COALESCE(ARGUMENT_SIGNATURE, '')
                    ))::INT AS OBJECT_ID
                    , FUNCTION_NAME || COALESCE(ARGUMENT_SIGNATURE, '()') AS OBJECT_NAME
                    , COMMENT AS DESCRIPTION
                    , FUNCTION_SCHEMA AS TABLE_SCHEMA
                    , 'FUNCTION' AS OBJECT_TYPE
                    , COALESCE(FUNCTION_OWNER, '') AS OWNER
                    , CREATED AS CREATEDATATIME
                FROM {functionsView}
                WHERE FUNCTION_SCHEMA <> 'INFORMATION_SCHEMA'
            )
            , sequence_objects AS
            (
                SELECT
                    (-3000000 - DENSE_RANK() OVER
                    (
                        ORDER BY SEQUENCE_SCHEMA, SEQUENCE_NAME
                    ))::INT AS OBJECT_ID
                    , SEQUENCE_NAME AS OBJECT_NAME
                    , COMMENT AS DESCRIPTION
                    , SEQUENCE_SCHEMA AS TABLE_SCHEMA
                    , 'SEQUENCE' AS OBJECT_TYPE
                    , COALESCE(SEQUENCE_OWNER, '') AS OWNER
                    , CREATED AS CREATEDATATIME
                FROM {sequencesView}
                WHERE SEQUENCE_SCHEMA <> 'INFORMATION_SCHEMA'
            )
            SELECT OBJECT_ID, OBJECT_NAME, DESCRIPTION, TABLE_SCHEMA, OBJECT_TYPE, OWNER, CREATEDATATIME
            FROM table_objects
            UNION ALL
            SELECT OBJECT_ID, OBJECT_NAME, DESCRIPTION, TABLE_SCHEMA, OBJECT_TYPE, OWNER, CREATEDATATIME
            FROM procedure_objects
            UNION ALL
            SELECT OBJECT_ID, OBJECT_NAME, DESCRIPTION, TABLE_SCHEMA, OBJECT_TYPE, OWNER, CREATEDATATIME
            FROM function_objects
            UNION ALL
            SELECT OBJECT_ID, OBJECT_NAME, DESCRIPTION, TABLE_SCHEMA, OBJECT_TYPE, OWNER, CREATEDATATIME
            FROM sequence_objects
            ORDER BY TABLE_SCHEMA, OBJECT_TYPE, OBJECT_NAME
            """;
    }

    protected override string GetSqlOfColumns(string dbName)
    {
        string tablesView = GetInformationSchemaObjectName(dbName, "TABLES");
        string columnsView = GetInformationSchemaObjectName(dbName, "COLUMNS");
        return
            $"""
            WITH table_objects AS
            (
                SELECT
                    DENSE_RANK() OVER
                    (
                        ORDER BY
                            TABLE_SCHEMA,
                            CASE
                                WHEN TABLE_TYPE IN ('VIEW', 'MATERIALIZED VIEW') THEN 'VIEW'
                                WHEN TABLE_TYPE = 'EXTERNAL TABLE' THEN 'EXTERNAL TABLE'
                                ELSE 'TABLE'
                            END,
                            TABLE_NAME
                    )::INT AS OBJECT_ID
                    , TABLE_CATALOG
                    , TABLE_SCHEMA
                    , TABLE_NAME
                FROM {tablesView}
                WHERE TABLE_SCHEMA <> 'INFORMATION_SCHEMA'
            )
            SELECT
                O.OBJECT_ID
                , C.COLUMN_NAME
                , C.COMMENT AS DESCRIPTION
                , C.DATA_TYPE
                    || COALESCE('(' || C.CHARACTER_MAXIMUM_LENGTH || ')',
                                '(' || C.NUMERIC_PRECISION || ',' || C.NUMERIC_SCALE || ')',
                                '')
                    || CASE WHEN C.IS_NULLABLE = 'NO' THEN ' NOT NULL' ELSE '' END AS TYPE_NAME
                , CASE WHEN C.IS_NULLABLE = 'NO' THEN TRUE ELSE FALSE END AS IS_NOT_NULL
                , C.COLUMN_DEFAULT
            FROM {columnsView} C
            JOIN table_objects O
                ON O.TABLE_CATALOG = C.TABLE_CATALOG
                AND O.TABLE_SCHEMA = C.TABLE_SCHEMA
                AND O.TABLE_NAME = C.TABLE_NAME
            ORDER BY O.OBJECT_ID, C.ORDINAL_POSITION
            """;
    }

    protected override List<(string, string)> GetDatabases()
    {
        List<(string, string)> databases = [];
        using var con = GetConnection(null);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = "SHOW DATABASES";

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            string databaseName = rdr.GetString(1);
            databases.Add((databaseName, DefaultSchemaName));
        }

        return databases;
    }

    protected override string? GetExternalTableSql(string database)
    {
        string tablesView = GetInformationSchemaObjectName(database, "TABLES");
        string escapedDatabase = EscapeSqlLiteral(database);
        return
            $"""
            SELECT
                TABLE_SCHEMA
                , TABLE_NAME
                , GET_DDL(
                    'TABLE',
                    '"{escapedDatabase}"."' || REPLACE(TABLE_SCHEMA, '"', '""') || '"."' || REPLACE(TABLE_NAME, '"', '""') || '"',
                    TRUE)
            FROM {tablesView}
            WHERE TABLE_TYPE = 'EXTERNAL TABLE'
                AND TABLE_SCHEMA <> 'INFORMATION_SCHEMA'
            """;
    }

    protected override string? GetProceduresSql(string database, string objectFilterName)
    {
        string proceduresView = GetInformationSchemaObjectName(database, "PROCEDURES");
        return
            $"""
            SELECT
                PROCEDURE_SCHEMA
                , COALESCE(PROCEDURE_DEFINITION, '') AS PROCEDURE_SOURCE
                , (-1000000 - DENSE_RANK() OVER
                    (
                        ORDER BY PROCEDURE_SCHEMA, PROCEDURE_NAME, COALESCE(ARGUMENT_SIGNATURE, '')
                    ))::INT AS PROCEDURE_ID
                , DATA_TYPE AS RETURNS
                , FALSE AS EXECUTE_AS_OWNER
                , COMMENT AS DESCRIPTION
                , PROCEDURE_NAME || COALESCE(ARGUMENT_SIGNATURE, '()') AS PROCEDURE_SIGNATURE
                , COALESCE(ARGUMENT_SIGNATURE, '()') AS ARGUMENTS
                , PROCEDURE_LANGUAGE
            FROM {proceduresView}
            WHERE PROCEDURE_SCHEMA <> 'INFORMATION_SCHEMA'
            ORDER BY PROCEDURE_SCHEMA, PROCEDURE_SIGNATURE
            """;
    }

    protected override string? GetViewsSql(string database, string objectFilterName)
    {
        string viewsView = GetInformationSchemaObjectName(database, "VIEWS");
        return
            $"""
            SELECT
                TABLE_SCHEMA
                , TABLE_NAME
                , VIEW_DEFINITION
            FROM {viewsView}
            WHERE TABLE_SCHEMA <> 'INFORMATION_SCHEMA'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;
    }

    public override void ChangeDatabaseSpecial(DbConnection con, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        if (con.State != System.Data.ConnectionState.Open)
        {
            con.Open();
        }

        using var command = con.CreateCommand();
        command.CommandText = $"USE DATABASE {QuoteNameIfNeeded(databaseName)}";
        _ = command.ExecuteNonQuery();

        if (!string.IsNullOrWhiteSpace(_schemaName))
        {
            command.CommandText = $"USE SCHEMA {QuoteNameIfNeeded(_schemaName)}";
            try
            {
                _ = command.ExecuteNonQuery();
            }
            catch (SnowflakeDbException ex) when (ShouldIgnoreMissingSchemaError(ex))
            {
            }
        }
    }

    private static bool ShouldIgnoreMissingSchemaError(SnowflakeDbException ex)
    {
        return string.Equals(ex.SqlState, "02000", StringComparison.Ordinal)
            && ex.ErrorCode == 2043;
    }

    protected override string? GetSynonymSql(string database)
    {
        throw new NotSupportedException("Snowflake does not support synonyms.");
    }

    public override async ValueTask GetCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null)
    {
        await AppendGetDdlAsync(sb, database, schema, tableName, "TABLE");
    }

    public override async ValueTask GetCreateViewTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName)
    {
        await AppendGetDdlAsync(stringBuilder, database, schema, tableName, "VIEW");
    }

    public override async ValueTask GetCreateProcedureTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName, bool forceFreshCode = false)
    {
        await AppendGetDdlAsync(stringBuilder, database, schema, tableName, "PROCEDURE", hasSignature: true);
    }

    public override async ValueTask GetCreateExternalTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName)
    {
        await AppendGetDdlAsync(stringBuilder, database, schema, tableName, "TABLE");
    }

    public override string GetCreateProcedureCall(string database, string schema, string tableName)
    {
        int signatureIndex = tableName.IndexOf('(');
        string procedureName = signatureIndex >= 0 ? tableName[..signatureIndex] : tableName;
        string argumentPlaceholder = signatureIndex >= 0 && tableName[signatureIndex..] != "()"
            ? "(<ARGUMENTS>)"
            : "()";
        var (cleanDatabase, cleanSchema) = GetCleanedNames(database, schema);
        string cleanProcedureName = QuoteNameIfNeeded(procedureName);
        return $"CALL {cleanDatabase}.{cleanSchema}.{cleanProcedureName}{argumentPlaceholder};";
    }

    public override string GetCreateProcedurePatternText()
    {
        return
            """
            CREATE OR REPLACE PROCEDURE <SCHEMA>.<PROCEDURE_NAME>()
            RETURNS VARCHAR
            LANGUAGE SQL
            AS
            $$
            BEGIN
                RETURN 'OK';
            END;
            $$;
            """;
    }

    private async ValueTask AppendGetDdlAsync(StringBuilder stringBuilder, string database, string schema, string objectName, string objectType, bool hasSignature = false)
    {
        string? ddl = await Task.Run(() =>
        {
            try
            {
                using var connection = GetConnection(database);
                connection.Open();

                using var command = connection.CreateCommand();
                string ddlTarget = hasSignature
                    ? GetQualifiedRoutineName(database, schema, objectName)
                    : GetQualifiedObjectName(database, schema, objectName);

                command.CommandText = $"SELECT GET_DDL('{objectType}', '{ddlTarget.Replace("'", "''")}', TRUE)";
                return command.ExecuteScalar() as string;
            }
            catch (Exception ex)
            {
                return $"-- Error retrieving DDL: {ex.Message}";
            }
        });

        if (!string.IsNullOrWhiteSpace(ddl))
        {
            stringBuilder.AppendLine(ddl);
        }
    }

    private string GetQualifiedObjectName(string database, string schema, string objectName)
    {
        var (cleanDatabase, cleanSchema, cleanObjectName) = GetCleanedNames(database, schema, objectName);
        return $"{cleanDatabase}.{cleanSchema}.{cleanObjectName}";
    }

    private string GetQualifiedRoutineName(string database, string schema, string routineNameWithSignature)
    {
        int signatureIndex = routineNameWithSignature.IndexOf('(');
        string routineName = signatureIndex >= 0 ? routineNameWithSignature[..signatureIndex] : routineNameWithSignature;
        string signature = signatureIndex >= 0 ? routineNameWithSignature[signatureIndex..] : "()";
        var (cleanDatabase, cleanSchema) = GetCleanedNames(database, schema);
        string cleanRoutineName = QuoteNameIfNeeded(routineName);
        return $"{cleanDatabase}.{cleanSchema}.{cleanRoutineName}{signature}";
    }

    private string GetInformationSchemaObjectName(string? databaseName, string objectName)
    {
        string resolvedDatabase = string.IsNullOrWhiteSpace(databaseName) ? Database : databaseName;
        return $"{QuoteNameIfNeeded(resolvedDatabase)}.INFORMATION_SCHEMA.{objectName}";
    }

    private static string NormalizeServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return string.Empty;
        }

        string trimmedServer = server.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmedServer, UriKind.Absolute, out Uri? uri))
        {
            return uri.Host;
        }

        return trimmedServer;
    }

    private static string ResolveAccountIdentifier(string normalizedServer)
    {
        if (normalizedServer.EndsWith(SnowflakeHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedServer[..^SnowflakeHostSuffix.Length];
        }

        return normalizedServer;
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static bool ShouldUseLiveTestFallback(LoginDataModel loginData)
    {
        string? liveTestAccount = Environment.GetEnvironmentVariable("SNOWFLAKE_LIVE_TEST_ACCOUNT");
        string? liveTestDatabase = Environment.GetEnvironmentVariable("SNOWFLAKE_LIVE_TEST_DATABASE");
        string? liveTestUser = Environment.GetEnvironmentVariable("SNOWFLAKE_LIVE_TEST_USER");

        return string.Equals(NormalizeServer(loginData.Server), NormalizeServer(liveTestAccount), StringComparison.OrdinalIgnoreCase)
            && string.Equals(loginData.Database, liveTestDatabase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(loginData.UserName, liveTestUser, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSetting(string? explicitValue, string environmentVariableName, bool allowEnvironmentFallback, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        if (allowEnvironmentFallback && Environment.GetEnvironmentVariable(environmentVariableName) is string environmentValue && !string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return defaultValue;
    }
}
