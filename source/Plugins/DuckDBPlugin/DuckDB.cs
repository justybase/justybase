using DuckDB.NET.Data;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using System.Data.Common;
using System.Text;

namespace DuckDBPlugin;
public sealed class DuckDB : DatabaseService
{
    public override DatabaseTypeEnum DatabaseType => DatabaseTypeEnum.DuckDB;

    public DuckDB(DbConnectionOptions options) : base(options)
    {
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseSchemaTable;
        preferDatabaseInCodes = false;
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        if (string.IsNullOrEmpty(Ip))
        {
            // DuckDB's current_database() returns a catalog name, which may
            // differ from the file path stored in the connection settings.
            // Reopen the configured file when the schema cache supplies that name.
            var dataSource = string.IsNullOrWhiteSpace(Database) || Database.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
                ? ":memory:?cache=shared"
                : Database;
            Connection = new DuckDBConnection(@$"Data Source={dataSource}");
        }
        else
        {
            databaseName ??= Database;
            Connection = new DuckDBConnection(@$"Data Source={Ip}\{databaseName}.db");
        }

        return Connection;
    }

    protected override string GetSqlTablesAndOtherObjects(string dbName)
    {
        return
            """
            SELECT
                table_oid AS OBJECT_ID,
                table_name AS OBJECT_NAME,
                comment AS DESCRIPTION,
                schema_name AS SCHEMA_NAME,
                'BASE TABLE' AS OBJECT_TYPE,
                '' AS OWNER,
                NULL AS CREATEDATATIME
            FROM duckdb_tables()
            WHERE NOT internal AND database_name = current_database()

            UNION ALL

            SELECT
                view_oid AS OBJECT_ID,
                view_name AS OBJECT_NAME,
                comment AS DESCRIPTION,
                schema_name AS SCHEMA_NAME,
                'VIEW' AS OBJECT_TYPE,
                '' AS OWNER,
                NULL AS CREATEDATATIME
            FROM duckdb_views()
            WHERE NOT internal AND database_name = current_database()
            """;
    }

    protected override string? GetProceduresSql(string database, string objectFilterName)
    {
        return
        """
        SELECT 
            "main" AS SCHEMA_
            , "sql"
           ,"rootpage"
            , UPPER("type")
            , NULL AS LANGUAGE
        FROM 
            sqlite_schema
        WHERE 
            name NOT LIKE 'sqlite_%'
            and "type" not in ('view','table','index')
        """;
    }

    protected override string? GetViewsSql(string database, string objectFilterName)
    {
        return
        """
        select schema_name, view_name, sql
        from duckdb_views()
        where not internal and database_name = current_database()
        """;
    }

    protected override List<(string, string)> GetDatabases()
    {
        var databases = new List<(string, string)>();
        var con = Connection;
        if (con.State != System.Data.ConnectionState.Open)
        {
            con.Open();
        }
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT current_database()";
        var databaseName = cmd.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            databases.Add((databaseName, "main"));
        }

        return databases;
    }

    protected override string GetSqlOfColumns(string dbName)
    {
        return
            """
                SELECT 
                    table_oid AS OBJECT_ID
                    , column_name
                    , comment AS DESCRIPTION
                    , data_type AS FULL_TYPE_NAME
                    , NOT is_nullable AS ATTNOTNULL
                    , column_default
                FROM duckdb_columns()
                WHERE NOT internal AND database_name = current_database()
                ORDER BY table_oid, column_index
                """;
    }

    protected override string? GetExternalTableSql(string database)
    {
        throw new NotImplementedException();
    }
    protected override string? GetSynonymSql(string database)
    {
        throw new NotImplementedException();
    }
    public override async ValueTask GetCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null)
    {
        await Task.Run(() =>
        {
            using var conn = GetConnection(database);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
            """
                SELECT sql FROM duckdb_tables()
                WHERE database_name = $database
                    AND schema_name = $schema
                    AND table_name = $tableName
            """;
            AddParameter(cmd, "database", database);
            AddParameter(cmd, "schema", schema);
            AddParameter(cmd, "tableName", tableName);
            string? txt = cmd.ExecuteScalar() as string;
            sb.Append(txt);
        });
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
