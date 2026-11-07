using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
// Namespace exported by the JustyBase.Db2.IBMCompat package.
using IBM.Data.Db2;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DB2Plugin;

public sealed class DB2DatabaseService : DatabaseService
{
    public override DatabaseTypeEnum DatabaseType => DatabaseTypeEnum.DB2;

    private readonly Db2ConnectionOptions? _db2Options;

    public DB2DatabaseService(DbConnectionOptions options) : base(options)
    {
        _db2Options = options.Db2Options;
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.SchemaTable;
        preferDatabaseInCodes = false;
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        databaseName ??= Database;

        DB2ConnectionStringBuilder builder = new DB2ConnectionStringBuilder();
        builder.UserID = Username;
        builder.Password = Password;

        builder.Server = Ip;
        // The managed JustyBase.Db2 driver parses "host:port" from Server (ParseServer).
        // An explicit Port is appended unless Ip already carries one
        // ("host:port", bracketed IPv6) — mirroring the MySql plugin pattern.
        if (!string.IsNullOrWhiteSpace(Ip) && !string.IsNullOrWhiteSpace(Port) && !Ip.Contains(':'))
        {
            builder.Server = $"{Ip.Trim()}:{Port.Trim()}";
        }
        builder.Database = databaseName;
        builder.Connect_Timeout = CONNECTION_TIMEOUT;
        builder.Pooling = pooling;

        if (_db2Options is { } db2Options)
        {
            if (!string.IsNullOrWhiteSpace(db2Options.CurrentSchema))
            {
                builder["CurrentSchema"] = db2Options.CurrentSchema.Trim();
            }

            if (db2Options.UseSsl)
            {
                builder.UseSSL = true;
            }

            if (db2Options.StatementTimeoutSeconds is int timeout && timeout > 0)
            {
                builder["StatementTimeout"] = timeout;
            }
        }

        //Connection = new DB2Connection(builder.ConnectionString);
        var conn = new DB2Connection(builder.ConnectionString);
        conn.InfoMessage += DB2Connection_InfoMessage;

        return conn;
    }

    private void DB2Connection_InfoMessage(object sender, DB2InfoMessageEventArgs e)
    {
        List<string> arr = [];
        for (int i = 0; i < e.Errors.Count; i++)
        {
            arr.Add(e.Errors[i].Message);
        }
        DbMessageAction?.Invoke(string.Join(Environment.NewLine, arr));
    }

    private readonly Dictionary<string, IReadOnlyList<DatabaseObject>> _tablespacesCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Db2 tablespaces are database-level objects, not schema children. The
    /// schema tree shows them under a dedicated "Tablespaces" node so they do
    /// not create a phantom empty schema.
    /// </summary>
    public override IEnumerable<DatabaseObject> GetTablespaces(string database)
    {
        string resolvedDatabase = string.IsNullOrWhiteSpace(database) ? Database : database;
        lock (_tablespacesCache)
        {
            if (_tablespacesCache.TryGetValue(resolvedDatabase, out var cached))
            {
                return cached;
            }
        }

        List<DatabaseObject> tablespaces = [];
        try
        {
            using var con = GetConnection(resolvedDatabase);
            con.Open();
            using var cmd = CreateCommandFromConnection(con);
            cmd.CommandText =
                """
                SELECT TRIM(T.TBSPACE) AS TBSPACE
                     , T.TBSPACEID
                     , TRIM(T.OWNER) AS OWNER
                     , T.TBSPACETYPE
                     , T.PAGESIZE
                     , COALESCE(T.REMARKS, '') AS REMARKS
                  FROM SYSCAT.TABLESPACES T
                 ORDER BY T.TBSPACE
                 WITH UR
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(0);
                int id = Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
                string owner = reader.GetString(2);
                string type = reader.GetString(3);
                int pageSize = Convert.ToInt32(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture);
                string remarks = reader.GetString(5);
                string management = type switch
                {
                    "D" => "Database managed (DMS)",
                    "S" => "System managed (SMS)",
                    _ => $"Unknown management ({type})",
                };
                string description = $"ID {id} | Owner {owner} | {management} | Page size {pageSize} bytes";
                if (!string.IsNullOrWhiteSpace(remarks))
                {
                    description += $" | {remarks}";
                }

                tablespaces.Add(new DatabaseObject(
                    id,
                    name,
                    description,
                    TypeInDatabaseEnum.Tablespace,
                    "TABLESPACE",
                    owner,
                    null));
            }
        }
        catch (Exception ex)
        {
            Logger.TrackError(ex, isCrash: false);
        }

        lock (_tablespacesCache)
        {
            _tablespacesCache[resolvedDatabase] = tablespaces;
        }

        return tablespaces;
    }

    public override void ClearCachedData()
    {
        base.ClearCachedData();
        lock (_tablespacesCache)
        {
            _tablespacesCache.Clear();
        }
    }

    protected override string GetSqlTablesAndOtherObjects(string dbName)
    {
        //SELECT TABNAME, TABSCHEMA, TABLEORG ,TBSPACE, COMPRESSION, keycolumns, keyindexid, keyunique, checkcount, PARTITION_MODE, REMARKS,PROPERTY FROM SYSCAT.TABLES
        //SELECT * FROM SYSIBM.SQLTABLES;
        return
        """

            SELECT
                   Y.NR
                   , Y.NAME
                   , Y.REMARKS
                   , Y.SCHEMA
                   , Y.TYPE
                   , Y.OWNER
                   , Y.CREATE_TIME
                   , Y.PARENT_NAME
             FROM
            (
                SELECT
                       CASE WHEN TYPE = 'PROCEDURE' THEN -1 ELSE ROW_NUMBER() OVER (ORDER BY SCHEMA, TYPE, NAME)::INT END AS NR
                       , NAME
                       , REMARKS
                       , SCHEMA
                       , TYPE
                       , TRIM(OWNER) AS OWNER
                       , CREATE_TIME
                       , CAST(NULL AS VARCHAR(257)) AS PARENT_NAME
                 FROM
                (
                    SELECT
                       TRIM(T.TABNAME) AS NAME
                       , T.REMARKS
                       , TRIM(T.TABSCHEMA) AS SCHEMA
                       , CASE
                            WHEN T.TYPE = 'T' THEN 'TABLE'
                            WHEN T.TYPE = 'U' THEN 'TYPED TABLE'
                            WHEN T.TYPE = 'H' THEN 'HIERARCHY TABLE'
                            WHEN T.TYPE = 'L' THEN 'DETACHED TABLE'
                            WHEN T.TYPE = 'S' THEN 'MATERIALIZED QUERY TABLE'
                            WHEN T.TYPE = 'V' THEN 'VIEW'
                            WHEN T.TYPE = 'W' THEN 'TYPED VIEW'
                            WHEN T.TYPE = 'N' THEN 'NICKNAME'
                            WHEN T.TYPE = 'A' THEN 'ALIAS'
                            WHEN T.TYPE = 'I' THEN 'INDEX'

                            ELSE 'OTHER TYPE'
                       END AS TYPE
                       , OWNER
                       , CREATE_TIME
                    FROM
                        SYSCAT.TABLES T

                UNION ALL

                select
                       TRIM(R.ROUTINENAME) || ' - ' ||R.PARM_COUNT::VARCHAR(5) AS NAME
                       , R.REMARKS
                       , TRIM(R.ROUTINESCHEMA) AS SCHEMA
                          , CASE
                            WHEN R.ROUTINETYPE = 'F' THEN 'FUNCTION'
                            WHEN R.ROUTINETYPE = 'M' THEN 'METHOD'
                            WHEN R.ROUTINETYPE = 'P' THEN 'PROCEDURE'
                            ELSE 'OTHER TYPE'
                       END AS TYPE
                       , OWNER
                       , CREATE_TIME
                from
                SYSCAT.ROUTINES R

                UNION ALL

                SELECT
                   TRIM(S.SEQNAME) AS NAME
                   , S.REMARKS
                   , TRIM(S.SEQSCHEMA) AS SCHEMA
                                      , CASE
                                        WHEN S.SEQTYPE = 'A' THEN 'ALIAS'
                                        WHEN S.SEQTYPE = 'I' THEN 'IDENTITY SEQUENCE'
                                        WHEN S.SEQTYPE = 'S' THEN 'SEQUENCE'
                                        ELSE 'OTHER TYPE'
                                   END AS TYPE
                    , OWNER
                    , CREATE_TIME
                FROM SYSCAT.SEQUENCES S
                WHERE S.SEQTYPE IN ('A','S','I')
                ) X

                UNION ALL

                -- Non-column objects keep NR = -1 so the numbered subquery in
                -- GetSqlOfColumns stays aligned with table column intervals.
                SELECT
                       -1 AS NR
                       , TRIM(I.INDNAME) AS NAME
                       , CAST(NULL AS VARCHAR(254)) AS REMARKS
                       , TRIM(I.INDSCHEMA) AS SCHEMA
                       , 'INDEX' AS TYPE
                       , CAST('' AS VARCHAR(128)) AS OWNER
                       , CAST(NULL AS TIMESTAMP) AS CREATE_TIME
                       , TRIM(I.TABNAME) AS PARENT_NAME
                FROM SYSCAT.INDEXES I
                WHERE TRIM(I.INDSCHEMA) NOT LIKE 'SYS%'

                UNION ALL

                SELECT
                       -1 AS NR
                       , TRIM(T.TRIGNAME) AS NAME
                       , T.REMARKS
                       , TRIM(T.TRIGSCHEMA) AS SCHEMA
                       , 'TRIGGER' AS TYPE
                       , TRIM(T.OWNER) AS OWNER
                       , T.CREATE_TIME
                       , TRIM(T.TABNAME) AS PARENT_NAME
                FROM SYSCAT.TRIGGERS T
                WHERE TRIM(T.TRIGSCHEMA) NOT LIKE 'SYS%'

                UNION ALL

                SELECT
                       -1 AS NR
                       , TRIM(P.TABNAME) || '.' || TRIM(P.DATAPARTITIONNAME) AS NAME
                       , CAST(NULL AS VARCHAR(254)) AS REMARKS
                       , TRIM(P.TABSCHEMA) AS SCHEMA
                       , 'PARTITION' AS TYPE
                       , COALESCE(TRIM(TB.OWNER), CAST('' AS VARCHAR(128))) AS OWNER
                       , TB.CREATE_TIME
                       , TRIM(P.TABNAME) AS PARENT_NAME
                FROM SYSCAT.DATAPARTITIONS P
                LEFT JOIN SYSCAT.TABLES TB
                       ON TB.TABSCHEMA = P.TABSCHEMA AND TB.TABNAME = P.TABNAME
                WHERE TRIM(P.TABSCHEMA) NOT LIKE 'SYS%'
            ) Y
            ORDER BY Y.SCHEMA, Y.TYPE, Y.NAME
            WITH UR
""";
    }

    private readonly Dictionary<int, (string PROVIDER_TYPE_NAME, string CREATE_PARAMS, string SQL_TYPE_NAME)> dataTypes = new();

    protected override List<(string, string)> GetDatabases()
    {
        var databases = new List<(string, string)>();
        using (var con = GetConnection(Database))
        {
            con.Open();
            if (dataTypes.Count == 0)
            {
                var dt = con.GetSchema("DataTypes");

                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    var currentRow = dt.Rows[i];
                    var providerIdObj = currentRow["PROVIDER_TYPE"];
                    string? typeName = currentRow["PROVIDER_TYPE_NAME"]?.ToString();
                    string? createParams = currentRow["CREATE_PARAMS"]?.ToString();
                    string? sqlTypeName = currentRow["SQL_TYPE_NAME"]?.ToString();
                    if (providerIdObj is int providerId && typeName is string && sqlTypeName is not null && createParams is not null)
                    {
                        dataTypes[providerId] = (typeName, createParams, sqlTypeName);
                    }
                }
            }

            databases.Add((con.Database, "SCHEMA"));
        }
        return databases;
    }

    protected override string GetSqlOfColumns(string dbName)
    {
        return
            """
                SELECT 
                    X.NR
                    , C.COLNAME
                    , C.REMARKS
                , CASE WHEN UPPER(TYPENAME) IN('BINARY','VARBINARY','BLOB', 'CLOB','DBCLOB','CHARACTER', 'VARCHAR','GRAPHIC','VARGRAPHIC') then TYPENAME || '(' || LENGTH ||')'
                       WHEN UPPER(TYPENAME) IN('DECIMAL') then TYPENAME || '(' || LENGTH || ',' || SCALE || ')'
                         ELSE TYPENAME
                         END 
                    || CASE WHEN C.NULLS = 'Y' THEN '' ELSE ' NOT NULL' END
                    || CASE WHEN C.DEFAULT IS NOT NULL THEN ' DEFAULT ' || DEFAULT ELSE '' END
                    AS TYPENAME
                    , CASE WHEN C.NULLS = 'N' THEN 1 ELSE 0 END::INT
                    , C.DEFAULT
                FROM 
                    SYSCAT.COLUMNS C
                    JOIN
                    (
                            SELECT 
                                   CASE WHEN  TYPE = 'PROCEDURE' THEN -1 ELSE ROW_NUMBER() OVER (ORDER BY SCHEMA, TYPE, NAME)::INT END AS NR
                                   , OBJDB
                                   , NAME
                                   , REMARKS
                                   ,SCHEMA
                                   , TYPE
                             FROM 
                            (
                                SELECT 
                                   NULL AS OBJDB
                                   , TRIM(T.TABNAME) AS NAME
                                   , T.REMARKS
                                   , TRIM(T.TABSCHEMA) AS SCHEMA
                                   , CASE 
                                        WHEN T.TYPE = 'T' THEN 'TABLE'
                                        WHEN T.TYPE = 'U' THEN 'TYPED TABLE'
                                        WHEN T.TYPE = 'H' THEN 'HIERARCHY TABLE'
                                        WHEN T.TYPE = 'L' THEN 'DETACHED TABLE'
                                        WHEN T.TYPE = 'S' THEN 'MATERIALIZED QUERY TABLE'
                                        WHEN T.TYPE = 'V' THEN 'VIEW'
                                        WHEN T.TYPE = 'W' THEN 'TYPED VIEW'
                                        WHEN T.TYPE = 'N' THEN 'NICKNAME'
                                        WHEN T.TYPE = 'A' THEN 'ALIAS'
                                        WHEN T.TYPE = 'I' THEN 'INDEX'
                        
                                        ELSE 'OTHER TYPE'
                                   END AS TYPE
                                FROM 
                                    SYSCAT.TABLES T
                                UNION ALL 


                            select 
                                   NULL AS OBJDB
                                   , TRIM(R.SPECIFICNAME) AS NAME
                                   , R.REMARKS
                                   , TRIM(R.ROUTINESCHEMA) AS SCHEMA
                                      , CASE 
                                        WHEN R.ROUTINETYPE = 'F' THEN 'FUNCTION'
                                        WHEN R.ROUTINETYPE = 'M' THEN 'METHOD'      
                                        WHEN R.ROUTINETYPE = 'P' THEN 'PROCEDURE'      
                                        ELSE 'OTHER TYPE'
                                   END AS TYPE

                            from 
                            SYSCAT.ROUTINES R

                            UNION ALL
                            SELECT 
                               NULL AS OBJDB
                               , TRIM(S.SEQNAME) AS NAME
                               , S.REMARKS
                               , TRIM(S.SEQSCHEMA) AS SCHEMA
                                                  , CASE 
                                                    WHEN S.SEQTYPE = 'A' THEN 'ALIAS'
                                                    WHEN S.SEQTYPE = 'I' THEN 'IDENTITY SEQUENCE'
                                                    WHEN S.SEQTYPE = 'S' THEN 'SEQUENCE'                        
                                                    ELSE 'OTHER TYPE'
                                               END AS TYPE
                            FROM SYSCAT.SEQUENCES S
                            WHERE S.SEQTYPE IN ('A','S','I')
                            ) X 
                            ORDER BY SCHEMA, TYPE, NAME


                    ) X ON X.SCHEMA = C.TABSCHEMA AND X.NAME = C.TABNAME AND X.NR !=-1
                ORDER BY 
                    C.TABSCHEMA, C.TABNAME, C.COLNO
                WITH UR;
                """;
    }

    protected override string? GetProceduresSql(string database, string objectFilterName)
    {
        // SYSCAT.PROCEDURES is deprecated on current LUW releases and has no
        // SPECIFICNAME, so overloads collapse. ROUTINES keeps the specific name
        // and still exposes the stored SQL PL text for DDL/source search.
        return
            $"""
                SELECT TRIM(ROUTINESCHEMA), TEXT, -1
                    , '' AS RETURNS
                    , FALSE AS EXECUTEDASOWNER
                    , REMARKS
                    , TRIM(ROUTINENAME) || ' - ' || PARM_COUNT::VARCHAR(5) AS PROCEDURESIGNATURE
                    , '' AS ARGUMENTS
                    , LANGUAGE
                    , TRIM(SPECIFICNAME) AS SPECIFICNAME
                FROM SYSCAT.ROUTINES
                WHERE ROUTINETYPE = 'P'
                ORDER BY ROUTINESCHEMA, ROUTINENAME, PARM_COUNT, SPECIFICNAME
                WITH UR;
                """;
    }

    protected override string? GetViewsSql(string database, string objectFilterName)
    {
        return
            $"""
                SELECT TRIM(VIEWSCHEMA),TRIM(VIEWNAME), TEXT FROM SYSCAT.VIEWS
                    ORDER BY VIEWSCHEMA,VIEWNAME;
                """;
    }

    public override bool IsTypeInDatabaseSupported(TypeInDatabaseEnum typeInDatabase)
    {
        return typeInDatabase != TypeInDatabaseEnum.ExternalTable;
    }

    protected override string? GetSynonymSql(string database)
    {
        // Db2 exposes three "synonym-like" object families. Table aliases and
        // sequence aliases resolve through BASE_* columns (verified live on LUW);
        // nicknames keep the remote server/schema/table path.
        return
            """
            SELECT TRIM(TABSCHEMA) AS SCHEMA
                 , TRIM(TABNAME) AS NAME
                 , CAST(TRIM(BASE_TABNAME) AS VARCHAR(128)) AS REMOTE_TABLE
                 , CAST('' AS VARCHAR(128)) AS SERVERNAME
                 , CAST(TRIM(BASE_TABSCHEMA) AS VARCHAR(128)) AS REMOTE_SCHEMA
                 , REMARKS
              FROM SYSCAT.TABLES
             WHERE TYPE = 'A'
            UNION ALL
            SELECT TRIM(SEQSCHEMA) AS SCHEMA
                 , TRIM(SEQNAME) AS NAME
                 , CAST(TRIM(BASE_SEQNAME) AS VARCHAR(128)) AS REMOTE_TABLE
                 , CAST('' AS VARCHAR(128)) AS SERVERNAME
                 , CAST(TRIM(BASE_SEQSCHEMA) AS VARCHAR(128)) AS REMOTE_SCHEMA
                 , REMARKS
              FROM SYSCAT.SEQUENCES
             WHERE SEQTYPE = 'A'
            UNION ALL
            SELECT TRIM(A.TABSCHEMA) AS SCHEMA
                 , TRIM(A.TABNAME) AS NAME
                 , A.REMOTE_TABLE
                 , A.SERVERNAME
                 , NVL(A.REMOTE_SCHEMA, 'ADMIN') AS REMOTE_SCHEMA
                 , A.REMARKS
              FROM SYSCAT.NICKNAMES A;
            """;
    }

    public override async ValueTask<List<ProcedureCachedInfo>> GetProceduresSignaturesFromName(string database, string schema, string procName)
    {
        List<ProcedureCachedInfo> ll = [];
        if (!_procedureDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.Procedure], database);
        }

        if (_procedureDictCache.TryGetValue(database, out var d1) && d1.TryGetValue(schema, out var d2) &&
            d2.TryGetValue(procName, out var d3))
        {
            ll.Add(d3);
        }

        return ll;
    }

    public override async ValueTask GetCreateViewTextStringBuilder(StringBuilder sb, string database, string schema, string tableName)
    {
        if (!_viewDictCache.ContainsKey(database))
        {
            await CacheAllObjects(new TypeInDatabaseEnum[] { TypeInDatabaseEnum.View }, database);
        }
        if (_viewDictCache.TryGetValue(database, out var d1)
            && d1.TryGetValue(schema, out var d2)
            && d2.TryGetValue(tableName, out var d3)
            )
        {
            sb.Append(d3.ViewSource);
            return;
        }
        sb.Append("no view text avaiable");
    }

    public override async ValueTask GetCreateSynonymTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string synonymName)
    {
        // Db2 aliases are not returned by SYSCAT.NICKNAMES; resolve them from the
        // BASE_* columns first, then fall back to the nickname cache.
        string? aliasDdl = await Task.Run(() => TryGetAliasDdl(database, schema, synonymName));
        if (aliasDdl is not null)
        {
            stringBuilder.Append(aliasDdl);
            return;
        }

        if (!_synonymTableDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.Synonym], database);
        }

        if (_synonymTableDictCache.TryGetValue(database, out var d1) && d1.TryGetValue(schema, out var d2) &&
            d2.TryGetValue(synonymName, out var d3))
        {
            var f = GetQuotedTwoOrTreePartName(database, schema, synonymName);
            var g = GetQuotedTwoOrTreePartName(d3.RefObjNamePart1, d3.RefObjNamePart2, d3.RefObjNamePart3, force: true);
            stringBuilder.Append($"CREATE NICKNAME {f} FOR {g};");
            return;
        }
        stringBuilder.Append($"PROBLEM ! {database}.{schema}.{synonymName}");
    }

    private string? TryGetAliasDdl(string database, string schema, string aliasName)
    {
        string literalSchema = EscapeSqlLiteral(schema);
        string literalName = EscapeSqlLiteral(aliasName);
        try
        {
            using var con = GetConnection(database);
            con.Open();
            using var cmd = CreateCommandFromConnection(con);

            cmd.CommandText = $"""
                SELECT TRIM(BASE_TABSCHEMA), TRIM(BASE_TABNAME)
                  FROM SYSCAT.TABLES
                 WHERE TYPE = 'A'
                   AND TABSCHEMA = '{literalSchema}'
                   AND TABNAME = '{literalName}'
                 WITH UR
                """;
            using (var rdr = cmd.ExecuteReader())
            {
                if (rdr.Read())
                {
                    var alias = GetQuotedTwoOrTreePartName(null, schema, aliasName, force: true);
                    var target = GetQuotedTwoOrTreePartName(null, rdr.GetString(0), rdr.GetString(1), force: true);
                    return $"CREATE ALIAS {alias} FOR TABLE {target};{Environment.NewLine}";
                }
            }

            cmd.CommandText = $"""
                SELECT TRIM(BASE_SEQSCHEMA), TRIM(BASE_SEQNAME)
                  FROM SYSCAT.SEQUENCES
                 WHERE SEQTYPE = 'A'
                   AND SEQSCHEMA = '{literalSchema}'
                   AND SEQNAME = '{literalName}'
                 WITH UR
                """;
            using (var rdr = cmd.ExecuteReader())
            {
                if (rdr.Read())
                {
                    var alias = GetQuotedTwoOrTreePartName(null, schema, aliasName, force: true);
                    var target = GetQuotedTwoOrTreePartName(null, rdr.GetString(0), rdr.GetString(1), force: true);
                    return $"CREATE ALIAS {alias} FOR SEQUENCE {target};{Environment.NewLine}";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.TrackError(ex, isCrash: false);
        }

        return null;
    }

    public override async ValueTask GetCreateProcedureTextStringBuilder(StringBuilder sb, string database, string schema, string procName, bool forceFreshCode = false)
    {
        //var (cleanSchema, cleanTableName) = GetCleanedNames(schema, procName);

        List<ProcedureCachedInfo> list = await GetProceduresSignaturesFromName(database, schema, procName);
        if (list.Count == 0)
        {
            sb.Append(" NO SOURCE FOUND");
            return;
        }

        sb.Append(list[0].ProcedureSource);
    }


    public override async ValueTask GetCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null)
    {
        string SQL = $"SELECT TABLEORG ,TBSPACE, COMPRESSION, keycolumns, keyindexid, keyunique, checkcount, PARTITION_MODE, REMARKS,PROPERTY FROM SYSCAT.TABLES WHERE TABNAME = '{tableName}' AND TABSCHEMA = '{schema}';";
        //0         1       2           3           4           5           6           7               8
        string? organize = null;
        string? tbSpace = null;
        string? compression = null;
        short keycolumns = 0;
        string? remarks = null;
        string pk = "????";
        string? pkComment = null;
        List<string> pkCols = [];
        string pkEnforced = "";
        string pkTrusted = "";
        string distributeInfo = "-- DISTIBUTE BY ... ";
        string partitionInfo = "";
        string PARTITION_MODE = "";//7
        string PROPERTY = ""; //9

        Dictionary<string, List<string>> fkCols = [];
        Dictionary<string, string?> fkComments = [];
        Dictionary<string, string> fkEnforced = [];
        Dictionary<string, string> fkTrusted = [];

        Dictionary<string, string> constraints = [];
        Dictionary<string, string> checks = [];
        Dictionary<string, string> checksEnforced = [];
        Dictionary<string, string> checksTrusted = [];

        List<string> indexes = [];
        List<string> triggers = [];

        var (clearSchema, clearTableName) = GetCleanedNames(schema, tableName);

        await Task.Run(() =>
        {
            using (var conn = GetConnection(database))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();
                    rdr.Read();
                    organize = rdr.GetString(0) == "R" ? "ROW" : "COLUMN";
                    var tmp = rdr.GetValue(1);
                    if (tmp != DBNull.Value && tmp is string str)
                    {
                        tbSpace = str;
                    }
                    compression = rdr.GetString(2) == "N" ? "NO" : "YES";
                    keycolumns = rdr.GetInt16(3);
                    if (rdr.GetValue(8) != DBNull.Value)
                    {
                        remarks = rdr.GetString(8);
                    }
                    PARTITION_MODE = rdr.GetString(7).Trim();
                    PROPERTY = rdr.GetString(9).Trim();
                    if (string.IsNullOrWhiteSpace(PARTITION_MODE))
                    {
                        distributeInfo = "";
                    }
                    else if (PARTITION_MODE == "H" && PROPERTY == "Y")
                    {
                        distributeInfo = $"DISTRIBUTE BY RANDOM";
                    }
                    else if (PARTITION_MODE == "H")
                    {
                        List<string> listOfHash = [];

                        string distSql = $@"SELECT colname from syscat.columns 
                            where TABSCHEMA = '{schema}' and TABNAME = '{tableName}' and partkeyseq !=0
                            order by partkeyseq with ur";

                        using (var cmd2 = conn.CreateCommand())
                        {
                            cmd2.CommandText = distSql;
                            using var rdrDist = cmd2.ExecuteReader();
                            while (rdrDist.Read())
                            {
                                listOfHash.Add(rdrDist.GetString(0));
                            }
                        }

                        distributeInfo = $"DISTRIBUTE BY HASH({string.Join(',', listOfHash)})";
                    }
                    rdr.Close();
                }

                if (keycolumns >= 1)
                {
                    SQL = GetConstraints1Sql(schema, tableName);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = SQL;
                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            pk = rdr.GetString(0);
                            pkComment = rdr.GetValue(2) == DBNull.Value ? null : rdr.GetString(2);
                            pkCols.Add(rdr.GetString(1));

                            pkEnforced = rdr.GetString(3);
                            pkTrusted = rdr.GetString(4);

                            pkEnforced = pkEnforced switch
                            {
                                "Y" => " ENFORCED",
                                "N" => " NOT ENFORCED",
                                _ => "",
                            };
                            pkTrusted = pkTrusted switch
                            {
                                "Y" => " TRUSTED",
                                "N" => " NOT TRUSTED",
                                _ => "",
                            };
                        }

                        rdr.Close();
                    }
                }

                SQL = $@"SELECT c.constname
                             , kcu.colname
                             , c.remarks
                             , c.ENFORCED
                             , c.TRUSTED
                          FROM syscat.tabconst c
                               , syscat.keycoluse kcu
                         WHERE c.tabschema = '{schema}'
                               and c.TABNAME = '{tableName}'
                               AND c.type = 'F'
                           AND kcu.constname = c.constname
                           AND kcu.tabschema = c.tabschema
                           AND kcu.tabname   = c.tabname
                         ORDER BY c.constname
                                , kcu.colseq
                        WITH UR";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string constname = rdr.GetString(0);
                        string colname = rdr.GetString(1);
                        string? remark = rdr.GetValue(2) == DBNull.Value ? null : rdr.GetString(2);

                        if (!fkCols.TryGetValue(constname, out List<string>? value1))
                        {
                            value1 = [];
                            fkCols[constname] = value1;
                        }

                        value1.Add(colname);
                        fkComments[constname] = remark;

                        string fkEnforcedVal = rdr.GetString(3);
                        string fkTrustedVal = rdr.GetString(4);

                        fkEnforced[constname] = fkEnforcedVal switch
                        {
                            "Y" => " ENFORCED",
                            "N" => " NOT ENFORCED",
                            _ => "",
                        };
                        fkTrusted[constname] = fkTrustedVal switch
                        {
                            "Y" => " TRUSTED",
                            "N" => " NOT TRUSTED",
                            _ => "",
                        };
                    }
                    rdr.Close();
                }


                SQL = $@"SELECT
                        r.CONSTNAME,REFTABSCHEMA,REFTABNAME, PK_COLNAMES, DELETERULE, UPDATERULE
                    FROM
                        syscat.references r
                    WHERE
                        R.TABSCHEMA = '{schema}'
                         AND r.tabname = '{tableName}'";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string constname = rdr.GetString(0);
                        string REFTABSCHEMA = rdr.GetString(1).Trim();
                        //QuoteNameIfNeeded(ref REFTABSCHEMA);

                        string REFTABNAME = rdr.GetString(2);
                        string PK_COLNAMES = rdr.GetString(3);
                        string DELETERULEpom = rdr.GetString(4);
                        string UPDATERULE = rdr.GetString(5);
                        string deleterule = DELETERULEpom switch
                        {
                            "R" => "ON DELETE RESTRICT",
                            "C" => "ON DELETE CASCADE",
                            "N" => "ON DELETE SET NULL",
                            _ => $"ON DELETE {UPDATERULE} ???",
                        };
                        constraints[constname] = $"REFERENCES {QuoteNameIfNeeded(REFTABSCHEMA)}.{QuoteNameIfNeeded(REFTABNAME)}({Regex.Replace(PK_COLNAMES.Trim(), @"\s{3,}", ",")}) {deleterule}";
                    }
                    rdr.Close();
                }

                SQL = GetChecksSql(schema, tableName);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string constname = rdr.GetString(0);
                        checks[constname] = rdr.GetString(1);

                        string fkEnforcedVal = rdr.GetString(2);
                        string fkTrustedVal = rdr.GetString(3);

                        fkEnforcedVal = fkEnforcedVal switch
                        {
                            "Y" => " ENFORCED",
                            "N" => " NOT ENFORCED",
                            _ => "",
                        };
                        fkTrustedVal = fkTrustedVal switch
                        {
                            "Y" => " TRUSTED",
                            "N" => " NOT TRUSTED",
                            _ => "",
                        };
                        checksEnforced[constname] = fkEnforcedVal;
                        checksTrusted[constname] = fkTrustedVal;
                    }
                }
                //  ase uniquerule
                //    when 'P' then 'Primary key'
                //    when 'U' then 'Unique'
                //    when 'D' then 'Nonunique'
                //end as type,

                SQL = $@"SELECT INDSCHEMA, INDNAME, COLNAMES,
                     uniquerule,
                    case indextype 
                        when 'BLOK' then 'Block index'
                        when 'CLUS' then 'Clustering index'
                        when 'DIM' then 'Dimension block index'
                        when 'REG' then 'Regular index'
                        when 'XPTH' then 'XML path index'
                        when 'XRGN' then 'XML region index'
                        when 'XVIL' then 'Index over XML column (logical)'
                        when 'XVIP' then 'Index over XML column (physical)'
                    end as index_type
                    , COMPRESSION
                    FROM syscat.indexes  WHERE indschema not like 'SYS%' AND TABSCHEMA = '{schema}' AND TABNAME = '{tableName}' WITH UR;";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        string indshema = rdr.GetString(0).Trim();
                        string indName = rdr.GetString(1);
                        string compressIndexText = "";

                        //QuoteNameIfNeeded(ref indshema);


                        var indexColsList = rdr.GetString(2).Split('+');
                        for (int i = 0; i < indexColsList.Length; i++)
                        {
                            indexColsList[i] = QuoteNameIfNeeded(indexColsList[i]);
                        }
                        ArraySegment<string> indexArraySegment;
                        if (string.IsNullOrWhiteSpace(indexColsList[0]))
                        {
                            indexArraySegment = new ArraySegment<string>(indexColsList, 1, indexColsList.Length - 1);
                        }
                        else
                        {
                            indexArraySegment = new ArraySegment<string>(indexColsList, 0, indexColsList.Length);
                        }

                        string uniquerule = rdr.GetString(3);
                        if (uniquerule == "U")
                        {
                            uniquerule = " UNIQUE";
                        }
                        else
                        {
                            uniquerule = "";
                        }
                        var tmp = rdr.GetValue(5);
                        if (tmp != DBNull.Value && tmp is string str && str == "Y")
                        {
                            compressIndexText = " COMPRESS YES";
                        }

                        indexes.Add($"CREATE{uniquerule} INDEX {QuoteNameIfNeeded(indshema)}.{QuoteNameIfNeeded(indName)} ON {clearSchema}.{clearTableName}({string.Join(',', (IEnumerable<string>)indexArraySegment)}){compressIndexText};");
                    }
                }

                SQL = GetTriggersSql(schema, tableName);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        triggers.Add(rdr.GetString(6));
                    }
                }


                SQL = "SELECT DATAPARTITIONEXPRESSION, NULLSFIRST FROM SYSCAT.DATAPARTITIONEXPRESSION " +
                    $"WHERE TABSCHEMA = '{schema}' AND TABNAME = '{tableName}' order by DATAPARTITIONKEYSEQ;";
                List<string> ls = [];
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = SQL;
                    var rdr = cmd.ExecuteReader();

                    while (rdr.Read())
                    {
                        var temp = rdr.GetValue(1);
                        string nullInfo = "";
                        if (temp != DBNull.Value && temp is string str && str == "N")
                        {
                            nullInfo = " NULLS LAST";
                        }
                        ls.Add(rdr.GetString(0) + nullInfo);
                    }
                }
                if (ls.Count > 0)
                {
                    partitionInfo = $"PARTITION BY RANGE({string.Join(',', ls)}){Environment.NewLine}";

                    SQL = $@"SELECT P.DATAPARTITIONNAME,P.LOWVALUE,P.HIGHVALUE,P.LOWINCLUSIVE,P.HIGHINCLUSIVE, S.TBSPACE
                        FROM SYSCAT.DATAPARTITIONS P 
                        LEFT JOIN syscat.tablespaces S ON S.TBSPACEID = P.TBSPACEID
                        WHERE P.TABSCHEMA = '{schema}' AND P.TABNAME = '{tableName}' ORDER BY SEQNO 
                        WITH UR;";

                    bool multiKey = false;
                    if (ls.Count > 1)
                    {
                        multiKey = true;
                    }

                    ls.Clear();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = SQL;
                        var rdr = cmd.ExecuteReader();
                        //int i = 0;
                        while (rdr.Read())
                        {
                            string DATAPARTITIONNAME = rdr.GetString(0);
                            string LOWVALUE = multiKey ? $"({rdr.GetValue(1)})" : $"{rdr.GetValue(1)}";
                            string HIGHVALUE = multiKey ? $"({rdr.GetValue(2)})" : $"{rdr.GetValue(2)}";
                            string LOWINCLUSIVE = rdr.GetString(3) == "Y" ? "INCLUSIVE" : "EXCLUSIVE";
                            string HIGHINCLUSIVE = rdr.GetString(4) == "Y" ? "INCLUSIVE" : "EXCLUSIVE";
                            string TBSPACE = rdr.GetString(5);
                            //i++;
                            ls.Add($"PARTITION {DATAPARTITIONNAME} STARTING FROM {LOWVALUE} {LOWINCLUSIVE} ENDING AT {HIGHVALUE} {HIGHINCLUSIVE} IN {TBSPACE}");
                        }
                        partitionInfo += $"({string.Join($",{Environment.NewLine}", ls)})";
                    }
                }
                conn.Close();
            }
        });

        DatabaseColumn[] columnsOfTable = GetColumns(database, schema, tableName, "").ToArray();


        string[] columnsWithTypes = new string[columnsOfTable.Length];
        for (int i = 0; i < columnsWithTypes.Length; i++)
        {
            columnsWithTypes[i] = $"{QuoteNameIfNeeded(columnsOfTable[i].Name)} {columnsOfTable[i].FullTypeName}";
        }

        sb.AppendLine($"CREATE TABLE {clearSchema}.{clearTableName}");
        sb.AppendLine("(");
        sb.Append("    ");
        sb.AppendLine(string.Join($",{Environment.NewLine}    ", columnsWithTypes));
        sb.AppendLine(")");
        if (tbSpace != null)
        {
            sb.AppendLine($"ORGANIZE BY {organize} IN {tbSpace}"); // SELECT XXX = TABLEORG , YYY = TBSPACE, ZZZ = COMPRESSION FROM SYSCAT.TABLES T WHERE T.TABNAME = 'EMPLOYEE'
        }
        else
        {
            sb.AppendLine($"ORGANIZE BY {organize}");
        }
        if (!string.IsNullOrWhiteSpace(distributeInfo))
        {
            sb.AppendLine(distributeInfo);
        }
        sb.AppendLine($"COMPRESS {compression}{Environment.NewLine}{partitionInfo};");

        //PK
        if (keycolumns >= 1)
        {
            string clearPk = QuoteNameIfNeeded(pk);
            sb.AppendLine($"ALTER TABLE {clearSchema}.{clearTableName} ADD CONSTRAINT {clearPk} PRIMARY KEY({string.Join(",", pkCols)}){pkEnforced}{pkTrusted};");
            if (pkComment is not null)
            {
                sb.AppendLine($"COMMENT ON CONSTRAINT {clearSchema}.{clearTableName}.{clearPk}  IS '{EscapeSqlLiteral(pkComment)}';");
            }
        }
        if (remarks != null)
        {
            sb.AppendLine($"COMMENT ON TABLE {clearSchema}.{clearTableName} IS '{EscapeSqlLiteral(remarks)}';");
        }
        // COMMENT ON CONSTRAINT TEST.EMPLOYEE.RED IS 'DDDD';


        foreach (var item in fkCols)
        {
            sb.AppendLine($"ALTER TABLE {clearSchema}.{clearTableName} ADD CONSTRAINT {item.Key} FOREIGN KEY({string.Join(",", item.Value)}) {constraints[item.Key]}{fkEnforced[item.Key]}{fkTrusted[item.Key]};");

            if (fkComments[item.Key] != null)
            {
                sb.AppendLine($"COMMENT ON CONSTRAINT {clearSchema}.{clearTableName}.{item.Key}  IS '{EscapeSqlLiteral(fkComments[item.Key]!)}';");
            }
        }

        foreach (var item in checks.Keys)
        {
            sb.AppendLine($"ALTER TABLE {clearSchema}.{clearTableName} ADD CONSTRAINT {item} CHECK({checks[item]}){checksEnforced[item]}{checksTrusted[item]};");
        }

        for (int i = 0; i < columnsOfTable.Length; i++)
        {
            if (columnsOfTable[i].Desc is not null)
            {
                sb.AppendLine($"COMMENT ON COLUMN {clearSchema}.{clearTableName}.{columnsOfTable[i].Name} IS '{EscapeSqlLiteral(columnsOfTable[i].Desc!)}';");
            }
        }

        foreach (var item in indexes)
        {
            sb.AppendLine(item);
        }

        if (triggers.Count > 0)
        {
            sb.AppendLine("--REGION TRIGGERS");
        }
        foreach (var item in triggers)
        {
            sb.AppendLine(item);
        }
        if (triggers.Count > 0)
        {
            sb.AppendLine("--ENDREGION");
        }

        //COMMENT ON COLUMN TEST.EMPLOYEE.EDLEVEL     IS 'highest grade level passed in school'
        // NOT NULL + 
        //Unique
        //Primary key + 
        //Foreign Key
        //Check
        //Informational
    }


    private static string GetConstraints1Sql(string schema, string tablename)
    {
        return $@"SELECT c.CONSTNAME, kcu.colname, c.remarks, c.ENFORCED, c.TRUSTED
                              FROM syscat.tabconst c
                                   , syscat.keycoluse kcu
                               WHERE c.tabschema = '{schema}'
                                     and c.TABNAME = '{tablename}'
                                    AND c.type = 'P'
                               AND kcu.constname = c.constname
                               AND kcu.tabschema = c.tabschema
                               AND kcu.tabname   = c.tabname
                             ORDER BY c.constname
                                    , kcu.colseq
                            WITH UR
                            ;";
    }
    private static string GetTriggersSql(string schema, string tablename)
    {
        return $@"select 
                                    trigname as trigger_name,
                                    tabschema , 
                                    tabname , 
                                    case trigtime 
                                         when 'B' then 'before'
                                         when 'A' then 'after'
                                         when 'I' then 'instead of' 
                                    end as activation,
                                    rtrim(case when eventupdate ='Y' then  'update ' else '' end 
                                          concat 
                                          case when eventdelete ='Y' then  'delete ' else '' end
                                          concat
                                          case when eventinsert ='Y' then  'insert ' else '' end)
                                    as event,   
                                    case when ENABLED = 'N' then 'disabled'
                                    else 'active' end as status,
                                    text as definition
                                from syscat.triggers t
                                where tabschema = '{schema}'
                                      AND  TABNAME = '{tablename}'
                                order by trigname";
    }

    /// <summary>Db2 LUW paginates with FETCH FIRST; LIMIT is rejected (-104).</summary>
    protected override string GetLimitClause(object rowsCnt) => $"FETCH FIRST {rowsCnt} ROWS ONLY";

    /// <summary>Db2 renames with RENAME TABLE; ALTER TABLE ... RENAME TO is rejected (-104).</summary>
    public override string GetTableRenameCode(string fullName) => $"RENAME TABLE {fullName} TO NEW_NAME;";

    /// <summary>Db2 does not accept the SQLite-style DROP ... IF EXISTS spelling.</summary>
    public override string GetDropIndex(string database, string schema, string index)
        => $"DROP INDEX {GetQuotedTwoOrTreePartName(null, schema, index, force: true)};";

    public override string GetDropTrigger(string database, string schema, string trigger)
        => $"DROP TRIGGER {GetQuotedTwoOrTreePartName(null, schema, trigger, force: true)};";

    /// <summary>Db2 CTAS requires WITH DATA instead of Netezza's DISTRIBUTE ON RANDOM.</summary>
    public override string GetCreateFromCode(string fullName)
        => $"CREATE TABLE ABC AS (SELECT T1.* FROM {fullName} AS T1) WITH DATA;";

    public override string GetAddComment(string table, string database, string schema)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"COMMENT ON TABLE {tableCl} IS '<comment>';";
    }

    /// <summary>Db2 LUW explains a statement with EXPLAIN PLAN FOR.</summary>
    public override string GetExplainCode(string database, string schema, string table)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"EXPLAIN PLAN FOR SELECT * FROM {tableCl};";
    }

    public override async ValueTask GetCreateIndexTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string indexName)
    {
        StringBuilder result = new();
        await Task.Run(() =>
        {
            using var conn = GetConnection(database);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TRIM(I.INDSCHEMA), TRIM(I.INDNAME), TRIM(I.TABSCHEMA), TRIM(I.TABNAME),
                       I.UNIQUERULE, I.INDEXTYPE, I.COMPRESSION
                  FROM SYSCAT.INDEXES I
                 WHERE I.INDSCHEMA = '{EscapeSqlLiteral(schema)}'
                   AND I.INDNAME = '{EscapeSqlLiteral(indexName)}'
                 WITH UR
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                result.AppendLine(CultureInfo.InvariantCulture, $"-- Index {schema}.{indexName} was not found.");
                return;
            }

            string indexSchema = reader.GetString(0);
            string foundIndex = reader.GetString(1);
            string tableSchema = reader.GetString(2);
            string tableName = reader.GetString(3);
            string uniqueRule = reader.GetString(4);
            string indexType = reader.GetString(5);
            string compression = reader.GetString(6);

            List<string> columns = [];
            using (var columnCmd = conn.CreateCommand())
            {
                columnCmd.CommandText = $"""
                    SELECT TRIM(COLNAME), COLORDER
                      FROM SYSCAT.INDEXCOLUSE
                     WHERE INDSCHEMA = '{EscapeSqlLiteral(indexSchema)}'
                       AND INDNAME = '{EscapeSqlLiteral(foundIndex)}'
                     ORDER BY COLSEQ
                     WITH UR
                    """;
                using var columnReader = columnCmd.ExecuteReader();
                while (columnReader.Read())
                {
                    string column = QuoteNameIfNeeded(columnReader.GetString(0));
                    string order = columnReader.IsDBNull(1) ? string.Empty : columnReader.GetString(1).Trim();
                    columns.Add(order == "D" ? $"{column} DESC" : column);
                }
            }

            string unique = uniqueRule == "U" ? " UNIQUE" : string.Empty;
            string cluster = indexType == "CLUS" ? " CLUSTER" : string.Empty;
            string compress = compression == "Y" ? " COMPRESS YES" : string.Empty;
            string qualifiedIndex = GetQuotedTwoOrTreePartName(null, indexSchema, foundIndex, force: true);
            string qualifiedTable = GetQuotedTwoOrTreePartName(null, tableSchema, tableName, force: true);
            result.AppendLine(CultureInfo.InvariantCulture, $"CREATE{unique} INDEX {qualifiedIndex} ON {qualifiedTable}({string.Join(", ", columns)}){cluster}{compress};");
        });

        stringBuilder.Append(result);
    }

    public override async ValueTask GetCreateTriggerTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string triggerName)
    {
        StringBuilder result = new();
        await Task.Run(() =>
        {
            using var conn = GetConnection(database);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TEXT
                  FROM SYSCAT.TRIGGERS
                 WHERE TRIGSCHEMA = '{EscapeSqlLiteral(schema)}'
                   AND TRIGNAME = '{EscapeSqlLiteral(triggerName)}'
                 WITH UR
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                result.AppendLine(CultureInfo.InvariantCulture, $"-- Trigger {schema}.{triggerName} was not found.");
                return;
            }

            string text = reader.GetString(0).Trim();
            result.AppendLine(text);
            if (!text.EndsWith(';'))
            {
                result.AppendLine(";");
            }
        });

        stringBuilder.Append(result);
    }

    public override async ValueTask GetCreatePartitionTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string partitionName)
    {
        StringBuilder result = new();
        await Task.Run(() =>
        {
            using var conn = GetConnection(database);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TRIM(P.TABSCHEMA), TRIM(P.TABNAME), TRIM(P.DATAPARTITIONNAME),
                       P.LOWVALUE, P.HIGHVALUE, P.LOWINCLUSIVE, P.HIGHINCLUSIVE,
                       COALESCE(TRIM(S.TBSPACE), '')
                  FROM SYSCAT.DATAPARTITIONS P
                  LEFT JOIN SYSCAT.TABLESPACES S ON S.TBSPACEID = P.TBSPACEID
                 WHERE P.TABSCHEMA = '{EscapeSqlLiteral(schema)}'
                   AND TRIM(P.TABNAME) || '.' || TRIM(P.DATAPARTITIONNAME) = '{EscapeSqlLiteral(partitionName)}'
                 WITH UR
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                result.AppendLine(CultureInfo.InvariantCulture, $"-- Partition {partitionName} was not found.");
                return;
            }

            string tableSchema = reader.GetString(0);
            string tableName = reader.GetString(1);
            string foundPartition = reader.GetString(2);
            string lowValue = reader.IsDBNull(3) ? "MINVALUE" : reader.GetValue(3).ToString() ?? "MINVALUE";
            string highValue = reader.IsDBNull(4) ? "MAXVALUE" : reader.GetValue(4).ToString() ?? "MAXVALUE";
            string lowInclusive = reader.IsDBNull(5) || reader.GetString(5) == "Y" ? "INCLUSIVE" : "EXCLUSIVE";
            string highInclusive = reader.IsDBNull(6) || reader.GetString(6) == "Y" ? "INCLUSIVE" : "EXCLUSIVE";
            string tablespace = reader.GetString(7);

            string qualifiedTable = GetQuotedTwoOrTreePartName(null, tableSchema, tableName, force: true);
            string tablespaceSuffix = string.IsNullOrWhiteSpace(tablespace) ? string.Empty : $" IN {tablespace}";
            result.AppendLine(CultureInfo.InvariantCulture, $"-- Partition {foundPartition} of table {qualifiedTable}.");
            result.AppendLine(CultureInfo.InvariantCulture, $"-- Range: STARTING FROM {lowValue} {lowInclusive} ENDING AT {highValue} {highInclusive}{tablespaceSuffix}.");
            result.AppendLine("-- Db2 creates range partitions through CREATE TABLE ... PARTITION BY RANGE or");
            result.AppendLine("-- ALTER TABLE ... ATTACH PARTITION with a matching staging table.");
        });

        stringBuilder.Append(result);
    }

    public override string GetCreateSynonymPatternText()
        => "CREATE ALIAS <schema>.<alias> FOR TABLE <schema>.<table>;";

    public override string GetCreateProcedureCall(string database, string schema, string tableName)
    {
        // Procedure objects are keyed as "NAME - PARAM_COUNT" so overloads stay
        // distinct in the tree; Db2 CALL itself takes "schema.name(args)".
        string name = tableName;
        string arguments = string.Empty;

        int paren = name.IndexOf('(');
        if (paren >= 0)
        {
            arguments = name[paren..];
            name = name[..paren];
        }
        else
        {
            int suffix = name.LastIndexOf(" - ", StringComparison.Ordinal);
            if (suffix > 0)
            {
                string countText = name[(suffix + 3)..].Trim();
                name = name[..suffix].Trim();
                if (int.TryParse(countText, out int count) && count > 0)
                {
                    arguments = "(" + string.Join(", ", Enumerable.Range(1, count).Select(i => $"<ARG{i}>")) + ")";
                }
            }
        }

        string qualified = GetQuotedTwoOrTreePartName(database, schema, name);
        return $"CALL {qualified}{arguments};";
    }

    /// <summary>Db2 maintenance equivalent of Netezza GROOM.</summary>
    public override string GetGroom(string database, string schema, string table)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"REORG TABLE {tableCl};";
    }

    /// <summary>Db2 maintenance equivalent of Netezza GENERATE STATISTICS.</summary>
    public override string GetGenerateStats(string database, string schema, string table)
    {
        string tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"RUNSTATS ON TABLE {tableCl} WITH DISTRIBUTION AND DETAILED INDEXES ALL;";
    }

    public override async ValueTask GetReCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName)
    {
        var ddl = new StringBuilder();
        await GetCreateTableTextStringBuilder(ddl, database, schema, tableName);

        string qualified = GetQuotedTwoOrTreePartName(database, schema, tableName);
        string backupName = BuildBackupTableName(tableName);
        string backupQualified = GetQuotedTwoOrTreePartName(database, schema, backupName);

        sb.AppendLine($"-- Recreate {qualified} in place. Review dependencies, privileges and the backup name before running.");
        sb.AppendLine("-- 1. Keep the current table as a backup (Db2 requires an unqualified rename target).");
        sb.AppendLine($"RENAME TABLE {qualified} TO {QuoteNameIfNeeded(backupName)};");
        sb.AppendLine();
        sb.AppendLine("-- 2. Create the replacement table.");
        sb.Append(ddl);
        sb.AppendLine();
        sb.AppendLine("-- 3. Copy the data.");
        sb.AppendLine($"INSERT INTO {qualified} SELECT * FROM {backupQualified};");
        sb.AppendLine();
        sb.AppendLine("-- 4. Refresh statistics.");
        sb.AppendLine($"RUNSTATS ON TABLE {qualified} WITH DISTRIBUTION AND DETAILED INDEXES ALL;");
        sb.AppendLine();
        sb.AppendLine($"-- 5. Drop {backupQualified} only after verifying the rebuilt table and its dependants.");
    }

    private static string BuildBackupTableName(string tableName)
    {
        string prefix = tableName.Length > 110 ? tableName[..110] : tableName;
        return $"{prefix}_BAK_{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private static string GetChecksSql(string schema, string tablename)
    {
        return $@"select con.CONSTNAME, con.text, tcu.ENFORCED, tcu.TRUSTED
                        from 
                            syscat.checks con
                            join syscat.tabconst tcu on tcu.CONSTNAME = con.CONSTNAME and tcu.TABSCHEMA= '{schema}' and con.tabname = '{tablename}'
                        where
                            con.TABSCHEMA = '{schema}'
                            and con.tabname = '{tablename}'
                    ";
    }

    public override async Task DbSpecificImportPart(IImportJob importJob, string randName, Action<string>? progress, bool tableExists = false)
    {
        await Task.Run(() =>
        {
            using var conn = GetConnection(null) as DB2Connection;
            if (conn is null)
            {
                return;
            }

            conn.Open();
            if (!tableExists)
            {
                string[] headers = importJob.ReturnHeadersWithDataTypes(DatabaseKind.Db2);
                string SQL = $"CREATE TABLE {randName} ({string.Join(',', headers)}){Environment.NewLine}ORGANIZE BY ROW{Environment.NewLine}COMPRESS YES{Environment.NewLine};";
                using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = SQL;
                cmd.ExecuteNonQuery();
            }

            using DB2BulkCopy cpy = new DB2BulkCopy(conn, DB2BulkCopyOptions.TableLock);
            cpy.BulkCopyTimeout = DEFAULT_COMMAND_TIMEOUT;
            cpy.DestinationTableName = randName;
            cpy.NotifyAfter = 10_000;
            cpy.DB2RowsCopied += (o, e) => progress?.Invoke($"Copied {e.RowsCopied:N0}");
            cpy.WriteToServer(importJob.AsReader);

            if (cpy.Errors.Count > 0)
            {
                if (progress is not null)
                {
                    progress?.Invoke($"{cpy.Errors.Count} Errors");
                    foreach (DB2Error item in cpy.Errors)
                    {
                        progress?.Invoke($"ERROR! Row: {item.RowNumber} Message:{item.Message}");
                    }
                    cpy.Close();
                    conn.Close();
                }
                else
                {
                    throw new Exception($"{cpy.Errors.Count} Errors");
                }
            }
            else
            {
                cpy.Close();
                conn.Close();
            }
        });
    }

    public override string GetEmpty(string table, string database, string schema)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);

        return @$"TRUNCATE TABLE {tableCl} IMMEDIATE;";
    }

    protected override string? GetExternalTableSql(string database)
    {
        throw new NotImplementedException();
    }
}
