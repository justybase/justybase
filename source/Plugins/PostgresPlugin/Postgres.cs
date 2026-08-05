using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using Npgsql;
using System.Data.Common;
using System.Text;

namespace PostgresPlugin;

public sealed class Postgres : DatabaseService
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.PostgreSql;

    private const string ColumnsSql =
        """
        SELECT
            c.oid::int AS rel_object_id,
            col.column_name,
            pg_catalog.col_description(c.oid, att.attnum) AS description,
            pg_catalog.format_type(att.atttypid, att.atttypmod)
                || CASE WHEN col.is_nullable = 'NO' THEN ' NOT NULL' ELSE '' END AS full_type_name,
            CASE WHEN col.is_nullable = 'NO' THEN TRUE ELSE FALSE END AS attnotnull,
            col.column_default
        FROM information_schema.columns col
        JOIN pg_catalog.pg_class c
            ON c.relname = col.table_name
        JOIN pg_catalog.pg_namespace n
            ON n.oid = c.relnamespace
            AND n.nspname = col.table_schema
        JOIN pg_catalog.pg_attribute att
            ON att.attrelid = c.oid
            AND att.attname = col.column_name
            AND att.attnum > 0
            AND NOT att.attisdropped
        WHERE col.table_schema NOT IN ('pg_catalog', 'information_schema')
          AND col.table_schema !~ '^pg_toast'
          AND col.table_schema !~ '^pg_temp'
        ORDER BY c.oid, col.ordinal_position;
        """;

    public Postgres(string username, string password, string port, string ip, string db, int connectionTimeout)
        : base(username, password, port, ip, db, connectionTimeout)
    {
        DatabaseType = WHO_I_AM_CONST;
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.DatabaseSchemaTable | CurrentAutoCompletDatabaseMode.SchemaTable;
        PrefrerUpperCase = false;
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        databaseName ??= Database;

        NpgsqlConnectionStringBuilder builder = new()
        {
            Username = Username,
            Password = Password,
            Host = Ip,
            Database = databaseName,
            Timeout = CONNECTION_TIMEOUT,
            Pooling = pooling
        };

        var conn = new NpgsqlConnection(builder.ConnectionString);
        conn.Notice += Conn_Notice;
        return conn;
    }

    private void Conn_Notice(object sender, NpgsqlNoticeEventArgs e)
    {
        DbMessageAction?.Invoke(e.Notice.MessageText);
    }

    public override void ChangeDatabaseSpecial(DbConnection con, string databaseName)
    {
        if (con.State != System.Data.ConnectionState.Open)
        {
            con.Open();
        }

        base.ChangeDatabaseSpecial(con, databaseName);
    }

    protected override string GetSqlTablesAndOtherObjects(string dbName)
    {
        return
            """
            SELECT
                c.oid::int AS object_id,
                c.relname AS object_name,
                obj_description(c.oid) AS description,
                n.nspname AS table_schema,
                CASE c.relkind
                    WHEN 'v' THEN 'VIEW'
                    WHEN 'm' THEN 'VIEW'
                    WHEN 'p' THEN 'TABLE'
                    WHEN 'r' THEN 'TABLE'
                    ELSE 'unknown table type'
                END AS table_type,
                pg_get_userbyid(c.relowner) AS owner,
                NULL::timestamp AS createdatetime
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('v', 'm', 'p', 'r')
              AND NOT c.relispartition
              AND c.relname IS NOT NULL
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname !~ '^pg_toast'
              AND n.nspname !~ '^pg_temp'

            UNION ALL

            SELECT
                -1 AS object_id,
                p.proname || '(' || COALESCE(pg_get_function_identity_arguments(p.oid), '') || ')' AS object_name,
                d.description AS description,
                n.nspname AS table_schema,
                CASE p.prokind
                    WHEN 'p' THEN 'PROCEDURE'
                    WHEN 'f' THEN 'FUNCTION'
                    ELSE 'FUNCTION'
                END AS table_type,
                pg_get_userbyid(p.proowner) AS owner,
                NULL::timestamp AS createdatetime
            FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            LEFT JOIN pg_catalog.pg_description d
                ON d.objoid = p.oid
                AND d.classoid = 'pg_proc'::regclass
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND p.prokind IN ('p', 'f')

            UNION ALL

            SELECT
                c.oid::int AS object_id,
                c.relname AS object_name,
                obj_description(c.oid) AS description,
                n.nspname AS table_schema,
                'SEQUENCE' AS table_type,
                pg_get_userbyid(c.relowner) AS owner,
                NULL::timestamp AS createdatetime
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'S'
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname !~ '^pg_toast'
              AND n.nspname !~ '^pg_temp'

            UNION ALL

            SELECT
                idx.oid::int AS object_id,
                idx.relname AS object_name,
                pg_get_indexdef(i.indexrelid) AS description,
                idx_ns.nspname AS table_schema,
                'INDEX' AS table_type,
                pg_get_userbyid(idx.relowner) AS owner,
                NULL::timestamp AS createdatetime
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
            JOIN pg_catalog.pg_class tbl ON tbl.oid = i.indrelid
            JOIN pg_catalog.pg_namespace idx_ns ON idx_ns.oid = idx.relnamespace
            LEFT JOIN pg_catalog.pg_constraint con ON con.conindid = i.indexrelid
            WHERE idx_ns.nspname NOT IN ('pg_catalog', 'information_schema')
              AND idx_ns.nspname !~ '^pg_toast'
              AND idx_ns.nspname !~ '^pg_temp'
              AND con.oid IS NULL

            UNION ALL

            SELECT
                child.oid::int AS object_id,
                child.relname AS object_name,
                format('PARTITION OF %I.%I %s', parent_ns.nspname, parent.relname, pg_get_expr(child.relpartbound, child.oid, true)) AS description,
                child_ns.nspname AS table_schema,
                'PARTITION' AS table_type,
                pg_get_userbyid(child.relowner) AS owner,
                NULL::timestamp AS createdatetime
            FROM pg_catalog.pg_inherits inh
            JOIN pg_catalog.pg_class parent ON parent.oid = inh.inhparent
            JOIN pg_catalog.pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
            JOIN pg_catalog.pg_class child ON child.oid = inh.inhrelid
            JOIN pg_catalog.pg_namespace child_ns ON child_ns.oid = child.relnamespace
            WHERE child_ns.nspname NOT IN ('pg_catalog', 'information_schema')
              AND child_ns.nspname !~ '^pg_toast'
              AND child_ns.nspname !~ '^pg_temp';
            """;
    }

    protected override string GetSqlOfColumns(string dbName)
    {
        return ColumnsSql;
    }

    protected override List<(string, string)> GetDatabases()
    {
        var databases = new List<(string, string)>();
        using var con = GetConnection(Database);
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText =
            """
            SELECT datname
            FROM pg_catalog.pg_database
            WHERE datallowconn
              AND NOT datistemplate
            ORDER BY datname;
            """;

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            databases.Add((rdr.GetString(0), "public"));
        }

        return databases;
    }

    protected override string? GetExternalTableSql(string database)
    {
        throw new NotImplementedException();
    }

    protected override string? GetProceduresSql(string database, string objectFilterName)
    {
        var escapedFilter = EscapeSqlLiteral(objectFilterName ?? string.Empty);
        var filterCondition = string.IsNullOrWhiteSpace(escapedFilter)
            ? string.Empty
            : $"AND p.proname ILIKE '%{escapedFilter}%'";

        return
            $"""
            SELECT
                n.nspname AS procedure_schema,
                pg_get_functiondef(p.oid) AS routine_definition,
                p.oid::int AS id,
                pg_catalog.format_type(p.prorettype, NULL) AS returns,
                p.prosecdef AS executedasowner,
                d.description AS description,
                p.proname || '(' || COALESCE(pg_get_function_identity_arguments(p.oid), '') || ')' AS procedure_signature,
                '(' || COALESCE(pg_get_function_identity_arguments(p.oid), '') || ')' AS arguments,
                l.lanname AS external_language
            FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            JOIN pg_catalog.pg_language l ON l.oid = p.prolang
            LEFT JOIN pg_catalog.pg_description d
                ON d.objoid = p.oid
                AND d.classoid = 'pg_proc'::regclass
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND p.prokind = 'p'
              {filterCondition};
            """;
    }

    public override async ValueTask GetCreateProcedureTextStringBuilder(StringBuilder sb, string database, string schema, string procName, bool forceFreshCode = false)
    {
        if (!_procedureDictCache.ContainsKey(database) || forceFreshCode)
        {
            await CacheAllObjects([TypeInDatabaseEnum.Procedure], database);
        }

        if (_procedureDictCache.TryGetValue(database, out var d1)
            && d1.TryGetValue(schema, out var d2)
            && d2.TryGetValue(procName, out var d3))
        {
            sb.AppendLine(d3.ProcedureSource);

            if (d3.Desc is not null)
            {
                string cmt = CleanComment(d3.Desc) ?? d3.Desc;
                sb.AppendLine();
                sb.AppendLine($"COMMENT ON PROCEDURE {procName} IS '{cmt}';");
            }
        }
    }

    public override string GetCreateProcedurePatternText()
    {
        return
            """
            CREATE OR REPLACE PROCEDURE public.my_procedure(IN p_id integer)
            LANGUAGE plpgsql
            AS $$
            BEGIN
                -- TODO: add implementation
                RAISE NOTICE 'Input id: %', p_id;
            END;
            $$;
            """;
    }

    public override string GetCreateProcedureCall(string database, string schema, string tableName)
    {
        var ext = "";
        if (tableName.EndsWith(')'))
        {
            int index = tableName.LastIndexOf('(');
            ext = tableName[index..];
            tableName = tableName[..tableName.IndexOf('(')];
        }

        var f = GetQuotedTwoOrTreePartName(database, schema, tableName);
        return $"CALL {f}{ext};";
    }

    protected override string? GetSynonymSql(string database)
    {
        throw new NotImplementedException();
    }

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
        await Task.Run(() =>
        {
            string outputTableName = overrideTableName ?? tableName;
            string schemaQuoted = QuoteNameIfNeeded(schema);
            string outputTableQuoted = QuoteNameIfNeeded(outputTableName);
            string sourceTableQuoted = QuoteNameIfNeeded(tableName);

            using var conn = GetConnection(database);
            conn.Open();

            List<string> columnLines = LoadColumnDefinitions(conn, schema, tableName);
            if (columnLines.Count == 0)
            {
                sb.AppendLine($"-- Table {schemaQuoted}.{sourceTableQuoted} was not found.");
                return;
            }

            List<string> constraintLines = LoadConstraintDefinitions(conn, schema, tableName);
            List<string> triggerStatements = LoadTriggerDefinitions(conn, schema, tableName);
            List<string> indexStatements = LoadIndexDefinitions(conn, schema, tableName);
            List<string> partitionChildStatements = LoadPartitionChildDefinitions(conn, schema, tableName);
            string partitionByClause = LoadPartitionByClause(conn, schema, tableName);

            sb.AppendLine($"CREATE TABLE {schemaQuoted}.{outputTableQuoted}");
            sb.AppendLine("(");

            var allTableLines = new List<string>(columnLines.Count + constraintLines.Count);
            allTableLines.AddRange(columnLines);
            allTableLines.AddRange(constraintLines);
            sb.AppendLine("    " + string.Join("," + Environment.NewLine + "    ", allTableLines));

            sb.Append(")");
            if (!string.IsNullOrWhiteSpace(partitionByClause))
            {
                sb.Append($" PARTITION BY {partitionByClause}");
            }

            sb.AppendLine(";");

            if (!string.IsNullOrWhiteSpace(middleCode))
            {
                sb.AppendLine(middleCode);
            }

            AppendSqlStatements(sb, indexStatements);
            AppendSqlStatements(sb, triggerStatements);
            AppendSqlStatements(sb, partitionChildStatements);

            if (!string.IsNullOrWhiteSpace(endingCode))
            {
                sb.AppendLine(endingCode);
            }

            sb.AppendLine();
        });
    }
    public override string GetCreateIndexPatternText(string database, string schema, string tableName)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, tableName);
        string indexName = QuoteNameIfNeeded($"ix_{tableName}_col1");

        return
            $"""
            CREATE INDEX {indexName}
                ON {tableCl} USING btree (<column_name>)
                INCLUDE (<covering_column_optional>)
                WHERE <optional_predicate>;
            """;
    }

    public override async ValueTask GetCreateIndexTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string indexName)
    {
        await Task.Run(() =>
        {
            using var conn = GetConnection(database);
            conn.Open();

            using var cmd = CreateSchemaNameCommand(conn,
                """
                SELECT pg_get_indexdef(i.indexrelid)
                FROM pg_catalog.pg_index i
                JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
                JOIN pg_catalog.pg_namespace ns ON ns.oid = idx.relnamespace
                WHERE ns.nspname = @schema
                  AND idx.relname = @name;
                """,
                schema,
                indexName);

            var ddlObj = cmd.ExecuteScalar();
            if (ddlObj is string ddl && !string.IsNullOrWhiteSpace(ddl))
            {
                stringBuilder.AppendLine(EnsureSqlStatement(ddl));
            }
            else
            {
                stringBuilder.AppendLine($"-- Index {QuoteNameIfNeeded(schema)}.{QuoteNameIfNeeded(indexName)} was not found.");
            }
        });
    }

    public override string GetCreatePartitionPatternText(string database, string schema, string tableName)
    {
        string schemaQuoted = QuoteNameIfNeeded(schema);
        string partitionName = QuoteNameIfNeeded($"{tableName}_p2026q1");
        string parentTable = GetQuotedTwoOrTreePartName(database, schema, tableName);

        return
            $"""
            CREATE TABLE {schemaQuoted}.{partitionName}
                PARTITION OF {parentTable}
                FOR VALUES FROM ('2026-01-01') TO ('2026-04-01');

            -- Alternative:
            -- CREATE TABLE {schemaQuoted}.{partitionName} PARTITION OF {parentTable}
            --     FOR VALUES IN ('A', 'B');
            """;
    }

    public override async ValueTask GetCreatePartitionTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string partitionName)
    {
        await Task.Run(() =>
        {
            using var conn = GetConnection(database);
            conn.Open();

            using var cmd = CreateSchemaNameCommand(conn,
                """
                SELECT
                    format(
                        'CREATE TABLE IF NOT EXISTS %I.%I PARTITION OF %I.%I %s;',
                        child_ns.nspname,
                        child.relname,
                        parent_ns.nspname,
                        parent.relname,
                        pg_get_expr(child.relpartbound, child.oid, true)) AS ddl
                FROM pg_catalog.pg_inherits inh
                JOIN pg_catalog.pg_class parent ON parent.oid = inh.inhparent
                JOIN pg_catalog.pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
                JOIN pg_catalog.pg_class child ON child.oid = inh.inhrelid
                JOIN pg_catalog.pg_namespace child_ns ON child_ns.oid = child.relnamespace
                WHERE child_ns.nspname = @schema
                  AND child.relname = @name;
                """,
                schema,
                partitionName);

            var ddlObj = cmd.ExecuteScalar();
            if (ddlObj is string ddl && !string.IsNullOrWhiteSpace(ddl))
            {
                stringBuilder.AppendLine(EnsureSqlStatement(ddl));
            }
            else
            {
                stringBuilder.AppendLine($"-- Partition {QuoteNameIfNeeded(schema)}.{QuoteNameIfNeeded(partitionName)} was not found.");
            }
        });
    }

    public override string GetPostgresIndexPartitionOverview(string database, string schema, string tableName)
    {
        string schemaLiteral = EscapeSqlLiteral(schema);
        string tableLiteral = EscapeSqlLiteral(tableName);

        return
            $"""
            -- 1) Indexes for selected table (including partition index descendants)
            SELECT
                ns.nspname AS table_schema,
                tbl.relname AS table_name,
                idx.relname AS index_name,
                pg_get_indexdef(i.indexrelid) AS index_ddl,
                CASE WHEN idx_inh.inhparent IS NULL THEN 'root_or_regular' ELSE 'partition_child' END AS index_scope
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
            JOIN pg_catalog.pg_class tbl ON tbl.oid = i.indrelid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = tbl.relnamespace
            LEFT JOIN pg_catalog.pg_inherits idx_inh ON idx_inh.inhrelid = idx.oid
            WHERE ns.nspname = '{schemaLiteral}'
              AND tbl.relname = '{tableLiteral}'
            ORDER BY index_scope, index_name;

            -- 2) Partition hierarchy and bounds for selected table
            SELECT
                parent_ns.nspname AS parent_schema,
                parent.relname AS parent_table,
                child_ns.nspname AS partition_schema,
                child.relname AS partition_table,
                pg_get_expr(child.relpartbound, child.oid, true) AS partition_bound
            FROM pg_catalog.pg_inherits inh
            JOIN pg_catalog.pg_class parent ON parent.oid = inh.inhparent
            JOIN pg_catalog.pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
            JOIN pg_catalog.pg_class child ON child.oid = inh.inhrelid
            JOIN pg_catalog.pg_namespace child_ns ON child_ns.oid = child.relnamespace
            WHERE parent_ns.nspname = '{schemaLiteral}'
              AND parent.relname = '{tableLiteral}'
            ORDER BY partition_schema, partition_table;

            -- 3) Indexes on partition tables
            SELECT
                child_ns.nspname AS partition_schema,
                child.relname AS partition_table,
                idx.relname AS partition_index,
                pg_get_indexdef(i.indexrelid) AS partition_index_ddl
            FROM pg_catalog.pg_inherits inh
            JOIN pg_catalog.pg_class parent ON parent.oid = inh.inhparent
            JOIN pg_catalog.pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
            JOIN pg_catalog.pg_class child ON child.oid = inh.inhrelid
            JOIN pg_catalog.pg_namespace child_ns ON child_ns.oid = child.relnamespace
            JOIN pg_catalog.pg_index i ON i.indrelid = child.oid
            JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
            WHERE parent_ns.nspname = '{schemaLiteral}'
              AND parent.relname = '{tableLiteral}'
            ORDER BY partition_schema, partition_table, partition_index;
            """;
    }

    public override string GetPostgresMaintenanceCommandPack(string database, string schema, string tableName)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, tableName);

        return
            $"""
            ANALYZE VERBOSE {tableCl};
            VACUUM (VERBOSE, ANALYZE) {tableCl};
            REINDEX TABLE {tableCl};
            -- Optional, less blocking but slower and only valid outside explicit transactions:
            -- REINDEX TABLE CONCURRENTLY {tableCl};
            """;
    }

    public override string GetGenerateStats(string database, string schema, string table)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"ANALYZE VERBOSE {tableCl};";
    }

    public override string GetGroom(string database, string schema, string table)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"VACUUM (VERBOSE, ANALYZE) {tableCl};";
    }

    public override string GetAddComment(string table, string database, string schema)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"COMMENT ON TABLE {tableCl} IS '<comment>';";
    }

    public override async ValueTask GetCreateViewTextStringBuilder(StringBuilder sb, string database, string schema, string tableName)
    {
        var (cleanDatabaseName, cleanSchema, cleanTableName) = GetCleanedNames(database, schema, tableName);
        sb.AppendLine($"CREATE OR REPLACE VIEW {cleanDatabaseName}.{cleanSchema}.{cleanTableName} AS ");

        if (!_viewDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.View], database);
        }

        if (_viewDictCache.TryGetValue(database, out var d1)
            && d1.TryGetValue(schema, out var d2)
            && d2.TryGetValue(tableName, out var d3))
        {
            sb.AppendLine(d3.ViewSource);
        }
    }

    public override async Task DbSpecificImportPart(IImportJob importJob, string randName, Action<string>? progress, bool tableExists = false)
    {
        await Task.Run(() =>
        {
            using var conn = GetConnection(null) as NpgsqlConnection;
            if (conn is null)
            {
                return;
            }

            conn.Open();
            if (!tableExists)
            {
                string[] headers = importJob.ReturnHeadersWithDataTypes(DatabaseKind.PostgreSql);
                string sql = $"CREATE TABLE {randName} ({string.Join(',', headers)});";
                using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            using var writer = conn.BeginBinaryImport($"COPY {randName} FROM STDIN (FORMAT BINARY)");

            var reader = importJob.AsReader;
            int fieldCount = reader.FieldCount;
            long rowCount = 0;

            while (reader.Read())
            {
                writer.StartRow();

                for (int i = 0; i < fieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        writer.Write(reader.GetValue(i));
                    }
                }

                rowCount++;
                if (rowCount % 10000 == 0)
                {
                    progress?.Invoke($"Copied {rowCount:N0}");
                }
            }

            try
            {
                writer.Complete();
            }
            catch (Exception ex)
            {
                progress?.Invoke($"ERROR! Message: {ex.Message}");
                throw;
            }
            finally
            {
                conn.Close();
            }
        });
    }
    private static NpgsqlCommand CreateSchemaNameCommand(DbConnection connection, string sql, string schema, string objectName)
    {
        var cmd = (NpgsqlCommand)connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("name", objectName);
        return cmd;
    }

    private List<string> LoadColumnDefinitions(DbConnection connection, string schema, string tableName)
    {
        const string sql =
            """
            SELECT
                col.column_name,
                pg_catalog.format_type(att.atttypid, att.atttypmod) AS data_type,
                col.is_nullable,
                col.column_default,
                col.collation_schema,
                col.collation_name
            FROM information_schema.columns col
            JOIN pg_catalog.pg_class c
                ON c.relname = col.table_name
            JOIN pg_catalog.pg_namespace n
                ON n.oid = c.relnamespace
                AND n.nspname = col.table_schema
            JOIN pg_catalog.pg_attribute att
                ON att.attrelid = c.oid
                AND att.attname = col.column_name
                AND att.attnum > 0
                AND NOT att.attisdropped
            WHERE col.table_schema = @schema
              AND col.table_name = @name
            ORDER BY col.ordinal_position;
            """;

        using var cmd = CreateSchemaNameCommand(connection, sql, schema, tableName);
        using var rdr = cmd.ExecuteReader();

        var result = new List<string>();
        while (rdr.Read())
        {
            string columnName = QuoteNameIfNeeded(rdr.GetString(0));
            string dataType = rdr.GetString(1);
            bool notNull = string.Equals(rdr.GetString(2), "NO", StringComparison.OrdinalIgnoreCase);

            string defaultClause = string.Empty;
            if (rdr.GetValue(3) is string colDefault && !string.IsNullOrWhiteSpace(colDefault))
            {
                defaultClause = $" DEFAULT {colDefault}";
            }

            string collationClause = string.Empty;
            if (rdr.GetValue(5) is string collationName && !string.IsNullOrWhiteSpace(collationName))
            {
                if (rdr.GetValue(4) is string collationSchema && !string.IsNullOrWhiteSpace(collationSchema))
                {
                    collationClause = $" COLLATE {QuoteNameIfNeeded(collationSchema)}.{QuoteNameIfNeeded(collationName)}";
                }
                else
                {
                    collationClause = $" COLLATE {QuoteNameIfNeeded(collationName)}";
                }
            }

            string nullClause = notNull ? " NOT NULL" : string.Empty;
            result.Add($"{columnName} {dataType}{collationClause}{defaultClause}{nullClause}");
        }

        return result;
    }

    private List<string> LoadConstraintDefinitions(DbConnection connection, string schema, string tableName)
    {
        const string sql =
            """
            SELECT
                con.conname,
                pg_catalog.pg_get_constraintdef(con.oid, true) AS definition
            FROM pg_catalog.pg_constraint con
            JOIN pg_catalog.pg_class rel ON rel.oid = con.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = rel.relnamespace
            WHERE n.nspname = @schema
              AND rel.relname = @name
            ORDER BY
                CASE con.contype
                    WHEN 'p' THEN 0
                    WHEN 'u' THEN 1
                    WHEN 'f' THEN 2
                    WHEN 'x' THEN 3
                    WHEN 'c' THEN 4
                    ELSE 5
                END,
                con.conname;
            """;

        using var cmd = CreateSchemaNameCommand(connection, sql, schema, tableName);
        using var rdr = cmd.ExecuteReader();

        var result = new List<string>();
        while (rdr.Read())
        {
            string constraintName = QuoteNameIfNeeded(rdr.GetString(0));
            string definition = rdr.GetString(1);
            result.Add($"CONSTRAINT {constraintName} {definition}");
        }

        return result;
    }

    private static List<string> LoadTriggerDefinitions(DbConnection connection, string schema, string tableName)
    {
        const string sql =
            """
            SELECT pg_catalog.pg_get_triggerdef(t.oid, true) AS trigger_ddl
            FROM pg_catalog.pg_trigger t
            JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema
              AND c.relname = @name
              AND NOT t.tgisinternal
            ORDER BY t.tgname;
            """;

        using var cmd = CreateSchemaNameCommand(connection, sql, schema, tableName);
        using var rdr = cmd.ExecuteReader();

        var result = new List<string>();
        while (rdr.Read())
        {
            string ddl = rdr.GetString(0);
            result.Add(EnsureSqlStatement(ddl));
        }

        return result;
    }

    private static List<string> LoadIndexDefinitions(DbConnection connection, string schema, string tableName)
    {
        const string sql =
            """
            SELECT pg_catalog.pg_get_indexdef(i.indexrelid) AS index_ddl
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
            JOIN pg_catalog.pg_class tbl ON tbl.oid = i.indrelid
            JOIN pg_catalog.pg_namespace ns ON ns.oid = tbl.relnamespace
            LEFT JOIN pg_catalog.pg_constraint con ON con.conindid = i.indexrelid
            WHERE ns.nspname = @schema
              AND tbl.relname = @name
              AND con.oid IS NULL
            ORDER BY idx.relname;
            """;

        using var cmd = CreateSchemaNameCommand(connection, sql, schema, tableName);
        using var rdr = cmd.ExecuteReader();

        var result = new List<string>();
        while (rdr.Read())
        {
            string ddl = rdr.GetString(0);
            result.Add(EnsureSqlStatement(ddl));
        }

        return result;
    }

    private static string LoadPartitionByClause(DbConnection connection, string schema, string tableName)
    {
        const string sql =
            """
            SELECT pg_catalog.pg_get_partkeydef(c.oid)
            FROM pg_catalog.pg_partitioned_table pt
            JOIN pg_catalog.pg_class c ON c.oid = pt.partrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema
              AND c.relname = @name;
            """;

        using var cmd = CreateSchemaNameCommand(connection, sql, schema, tableName);
        var res = cmd.ExecuteScalar();
        return res as string ?? string.Empty;
    }

    private static List<string> LoadPartitionChildDefinitions(DbConnection connection, string schema, string tableName)
    {
        const string sql =
            """
            SELECT
                format(
                    'CREATE TABLE IF NOT EXISTS %I.%I PARTITION OF %I.%I %s;',
                    child_ns.nspname,
                    child.relname,
                    parent_ns.nspname,
                    parent.relname,
                    pg_get_expr(child.relpartbound, child.oid, true)) AS ddl
            FROM pg_catalog.pg_inherits inh
            JOIN pg_catalog.pg_class parent ON parent.oid = inh.inhparent
            JOIN pg_catalog.pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
            JOIN pg_catalog.pg_class child ON child.oid = inh.inhrelid
            JOIN pg_catalog.pg_namespace child_ns ON child_ns.oid = child.relnamespace
            WHERE parent_ns.nspname = @schema
              AND parent.relname = @name
            ORDER BY child.relname;
            """;

        using var cmd = CreateSchemaNameCommand(connection, sql, schema, tableName);
        using var rdr = cmd.ExecuteReader();

        var result = new List<string>();
        while (rdr.Read())
        {
            result.Add(EnsureSqlStatement(rdr.GetString(0)));
        }

        return result;
    }

    private static void AppendSqlStatements(StringBuilder sb, IReadOnlyList<string> statements)
    {
        if (statements.Count == 0)
        {
            return;
        }

        foreach (string statement in statements)
        {
            sb.AppendLine(statement);
        }
    }

    private static string EnsureSqlStatement(string ddl)
    {
        string trimmed = ddl.TrimEnd();
        if (trimmed.EndsWith(';'))
        {
            return trimmed;
        }

        return trimmed + ";";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }
}
