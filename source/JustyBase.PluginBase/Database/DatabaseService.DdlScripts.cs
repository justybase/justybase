using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginCommons;
using JustyBase.PluginDatabaseBase.Models;
using System.Text;

namespace JustyBase.PluginDatabaseBase.Database;

public abstract partial class DatabaseService
{
    public virtual string GetTableDropCode(string fullName)
    {
        return $"DROP TABLE {fullName};";
    }
    public virtual string GetTableRenameCode(string fullName)
    {
        return $"ALTER TABLE {fullName} RENAME TO ABC;";
    }
    public virtual string GetShortSelectCode(string fullName)
    {
        return $"SELECT T1.* FROM {fullName} AS T1 {GetLimitClause(100)};";
    }
    public virtual string GetCreateFromCode(string fullName)
    {
        return $"CREATE TABLE ABC AS (SELECT T1.* FROM {fullName} AS T1) DISTRIBUTE ON RANDOM;";
    }

    protected string GetQuotedTwoOrTreePartName(string? database, string schema, string table, bool force = false)
    {
        if (!preferDatabaseInCodes && !force)
        {
            database = null;
        }

        if (table is not null)
        {
            table = QuoteNameIfNeeded(table);
        }
        if (database is not null)
        {
            database = QuoteNameIfNeeded(database);
        }
        if (schema is not null)
        {
            schema = QuoteNameIfNeeded(schema);
        }

        string tableCl;
        if (database is not null && schema is not null)
        {
            tableCl = $"{database}.{schema}.{table}";
        }
        else if (schema is not null)
        {
            tableCl = $"{schema}.{table}";
        }
        else
        {
            tableCl = $"{table}";
        }

        return tableCl;
    }
    public string GetTop100Select(string database, string schema, string table, bool snippetMode, bool addWhereToTextCols = false)
    {
        var cols = GetColumns(database, schema, table, "");
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);

        if (snippetMode)
        {
            var colList = string.Join("\r\n    , ",
                cols.Select(o =>
                {
                    return "${ALIAS}." + QuoteNameIfNeeded(o.Name);
                })
            );

            return $$"""
            SELECT 
                {{colList}}
            FROM {{tableCl}} ${ALIAS=T1}
            {{GetLimitClause(100)}}${Caret};
            """;
        }
        else
        {
            string aliasText = PrefrerUpperCase ? "T1" : "t1";
            var colList = string.Join("\r\n    , ", cols.Select(o => $"{aliasText}.{QuoteNameIfNeeded(o.Name)}"));
            string declareAddition = "";
            string whereAddition = "";

            if (addWhereToTextCols)
            {
                declareAddition = "declare &SEARCHED = UPPER('%${TEXT TO SEARCH}%');\r\n";
                var colsToWhere = cols.Where(o => o.FullTypeName.Contains("CHARACTER", StringComparison.OrdinalIgnoreCase)).Select(o =>
                {
                    return $"UPPER({aliasText}.{QuoteNameIfNeeded(o.Name)}) LIKE &SEARCHED";
                });
                if (!colsToWhere.Any())
                {
                    whereAddition =
                        """

                    where 1=2 -- no text columns
                    """;
                }
                else
                {
                    whereAddition =
                        $"""
                    
                WHERE 
                    --REGION WHERE CODE
                    {string.Join("\r\n  OR ", colsToWhere)}
                    --ENDREGION
                """;
                }
            }

            return $$"""
            {{declareAddition}}SELECT 
                --REGION COLS
                {{colList}}
                --ENDREGION
            FROM {{tableCl}} {{aliasText}}{{whereAddition}}
            {{GetLimitClause(100)}};
            """;
        }
    }

    public const string TABS_WITH_ROWS = "--##RETURN_ONLY_TABS_WITH_ROWS";
    public const string TIMEOUT_OVERRIDE = "--##TIMEOUT_OVERRIDE:";
    public const string CONTINUE_ON_ERROR = "--##CONTINUE_ON_ERROR";
    public string GetTop100SelectTextFromTables(string database, string schema, IEnumerable<DatabaseObject> tables)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(TABS_WITH_ROWS);
        sb.AppendLine($"{TIMEOUT_OVERRIDE}:20");
        sb.AppendLine(CONTINUE_ON_ERROR);
        sb.AppendLine(";declare &SEARCHED = UPPER('%${TEXT TO SEARCH}%');");
        sb.AppendLine("declare &LIMIT_CNT = 100;");
        string aliasText = PrefrerUpperCase ? "T1" : "t1";
        foreach (var item in tables)
        {
            var table = item.Name;
            var cols = GetColumns(database, schema, table, "");
            var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);

            var colList = string.Join("\r\n    , ", cols.Select(o => $"{aliasText}.{QuoteNameIfNeeded(o.Name)}"));

            string whereAddition = "";
            var colsToWhere = cols.Where(o => o.FullTypeName.Contains("CHARACTER", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("VARCHAR2", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase)
            || o.FullTypeName.StartsWith("NTEXT", StringComparison.OrdinalIgnoreCase)
            ).Select(o =>
            {
                return $"UPPER({aliasText}.{QuoteNameIfNeeded(o.Name)}) LIKE &SEARCHED";
            });
            if (!colsToWhere.Any())
            {
                continue;
            }
            else
            {
                whereAddition =
                    $"""
                    
                WHERE 
                    --REGION WHERE CODE
                    {string.Join("\r\n  OR ", colsToWhere)}
                    --ENDREGION
                """;
            }
            sb.AppendLine($$"""
            --REGION RESULT_NAME:{{tableCl}}
            SELECT 
                --REGION COLS
                {{colList}}
                --ENDREGION
            FROM {{tableCl}} {{aliasText}}{{whereAddition}}
            {{GetLimitClause("&LIMIT_CNT")}}
            --ENDREGION
            ;
            """);
        }

        return sb.ToString();
    }

    public string GetTop100SelectNumberFromTables(string database, string schema, IEnumerable<DatabaseObject> tables)
    {
        StringBuilder sb = new();
        sb.AppendLine(TABS_WITH_ROWS);
        sb.AppendLine($"{TIMEOUT_OVERRIDE}:20");
        sb.AppendLine(CONTINUE_ON_ERROR);
        sb.AppendLine(";declare &SEARCHED = ${123};");
        sb.AppendLine("declare &LIMIT_CNT = 100;");
        string aliasText = PrefrerUpperCase ? "T1" : "t1";
        foreach (var item in tables)
        {
            var table = item.Name;
            var cols = GetColumns(database, schema, table, "");
            var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);

            var colList = string.Join("\r\n    , ", cols.Select(o => $"{aliasText}.{QuoteNameIfNeeded(o.Name)}"));

            string whereAddition = "";
            var colsToWhere = cols.Where(o => o.FullTypeName.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("BYTEINT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("NUMBER", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("DOUBLE", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("REAL", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("DECFLOAT", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("MONEY", StringComparison.OrdinalIgnoreCase)
             || o.FullTypeName.StartsWith("SMALLMONEY", StringComparison.OrdinalIgnoreCase)
            ).Select(o =>
            {
                return $"{aliasText}.{QuoteNameIfNeeded(o.Name)} = &SEARCHED";
            });
            if (!colsToWhere.Any())
            {
                continue;
            }
            else
            {
                whereAddition =
                    $"""
                    
                WHERE 
                    --REGION WHERE CODE
                    {string.Join("\r\n  OR ", colsToWhere)}
                    --ENDREGION
                """;
            }
            sb.AppendLine($$"""
            --REGION RESULT_NAME:{{tableCl}}
            SELECT 
                --REGION COLS
                {{colList}}
                --ENDREGION
            FROM {{tableCl}} {{aliasText}}{{whereAddition}}
            {{GetLimitClause("&LIMIT_CNT")}}
            --ENDREGION
            ;
            """);
        }

        return sb.ToString();
    }

    public virtual string GetDuplicates(string table, string database, string schema)
    {
        var cols = GetColumns(database, schema, table, "");
        var colListString = cols.Select(o => QuoteNameIfNeeded(o.Name));
        var colList = string.Join("\r\n    , ", colListString.Append("COUNT(1)"));
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"""
            SELECT 
                {colList} 
            FROM {tableCl} 
            GROUP BY
                {string.Join("\r\n    , ", colListString)}
            HAVING
                COUNT(1) > 1
            {GetLimitClause(100)};
            """;
    }

    public virtual string GetDeleted(string table, string database, string schema)
    {
        return "not supported";
    }
    public virtual string GetGrant(string database, string schema, string table)
    {
        return "not supported";
    }
    public virtual string GetOrganize(string database, string schema, string table)
    {
        return "not supported";
    }
    public virtual string GetGroom(string database, string schema, string table)
    {
        return "not supported";
    }
    public virtual string GetDrop(string table, string database, string schema)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);

        return @$"DROP TABLE {tableCl};";
    }
    public virtual string GetEmpty(string table, string database, string schema)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);

        return @$"TRUNCATE TABLE {tableCl};";
    }
    public virtual string GetCountRows(string table, string database, string schema)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        return $"SELECT COUNT(*) FROM {tableCl};";
    }
    public virtual string GetExport(string table, string database, string schema)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, table);
        string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var invalids = Path.GetInvalidFileNameChars();
        var sanitizedName = string.Join("_", tableCl.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

        return @$"@expCsv: SELECT * FROM {tableCl} -> {path}\{sanitizedName}.csv;";
    }
    /// <summary>
    /// Legacy SQL stub removed. Import is handled by the Import document UI
    /// (<c>IActiveDocumentManager.OpenImportDocument</c> / Netezza named-pipe EXTERNAL).
    /// </summary>
    public virtual string GetImport(string table, string database, string schema)
    {
        return string.Empty;
    }

    public virtual string GetGenerateStats(string database, string schema, string table)
    {
        return "not supported";
    }

    public virtual string GetAddComment(string table, string database, string schema)
    {
        return "not supported";
    }

    public abstract ValueTask GetCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null);

    public async ValueTask<string> GetCreateTableText(string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null)
    {
        StringBuilder stringBuilder = new();
        await GetCreateTableTextStringBuilder(stringBuilder, database, schema, tableName, overrideTableName, middleCode, endingCode, distOverride);
        return stringBuilder.ToString();
    }

    public virtual async ValueTask GetReCreateTableTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName)
    {
        await Task.CompletedTask;
    }

    public async ValueTask<string> GetReCreateTableText(string database, string schema, string tableName)
    {
        StringBuilder stringBuilder = new();
        await GetReCreateTableTextStringBuilder(stringBuilder, database, schema, tableName);
        return stringBuilder.ToString();
    }

    public async ValueTask<string> GetCreateExternalText(string database, string schema, string tableName)
    {
        StringBuilder stringBuilder = new();
        await GetCreateExternalTextStringBuilder(stringBuilder, database, schema, tableName);
        return stringBuilder.ToString();
    }

    public virtual async ValueTask GetCreateExternalTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName)
    {
        await Task.CompletedTask;
    }

    public async ValueTask<string> GetCreateSynonymText(string database, string schema, string synonymName)
    {
        StringBuilder stringBuilder = new();
        await GetCreateSynonymTextStringBuilder(stringBuilder, database, schema, synonymName);
        return stringBuilder.ToString();
    }

    public virtual async ValueTask GetCreateSynonymTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string synonymName)
    {
        if (!_synonymTableDictCache.ContainsKey(database))
        {
            await CacheAllObjects([TypeInDatabaseEnum.Synonym], database);
        }

        if (_synonymTableDictCache.TryGetValue(database, out var d1) && d1.TryGetValue(schema, out var d2) && d2.TryGetValue(synonymName, out var d3))
        {
            var f = GetQuotedTwoOrTreePartName(database, schema, synonymName);
            var g = GetQuotedTwoOrTreePartName(d3.RefObjNamePart1, d3.RefObjNamePart2, d3.RefObjNamePart3, force: true);
            stringBuilder.Append($"CREATE SYNONYM {f} FOR {g};");
            return;
        }
        stringBuilder.Append($"PROBLEM ! {database}.{schema}.{synonymName}");
    }


    public string GetCreateSynonymPatternText()
    {
        return "CREATE SYNONYM <synonym> FOR <name>";
    }

    public string GetCreateSequencePatternText()
    {
        return
            """         
            CREATE SEQUENCE CUSTOMER_112 AS BIGINT 
               START WITH 1 
               INCREMENT BY 1 
               MINVALUE 0
               NO MAXVALUE
               NO CYCLE;
            """;
    }

    public virtual string GetCreateIndexPatternText(string database, string schema, string tableName)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, tableName);
        var indexName = QuoteNameIfNeeded($"IX_{tableName}");
        return $"CREATE INDEX {indexName} ON {tableCl} (<COL1>);";
    }

    public virtual string GetCreatePartitionPatternText(string database, string schema, string tableName)
    {
        var tableCl = GetQuotedTwoOrTreePartName(database, schema, tableName);
        return
            $"""
            -- Partition template is database specific.
            -- Base table:
            -- {tableCl}
            """;
    }

    public virtual string GetCreateProcedurePatternText()
    {
        return
            """
            -- TO DO SAMPLE PROCEDURE...
            """;
    }

    public async ValueTask<string> GetCreateIndexText(string database, string schema, string indexName)
    {
        StringBuilder stringBuilder = new();
        await GetCreateIndexTextStringBuilder(stringBuilder, database, schema, indexName);
        return stringBuilder.ToString();
    }

    public virtual async ValueTask GetCreateIndexTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string indexName)
    {
        await Task.CompletedTask;
    }

    public async ValueTask<string> GetCreatePartitionText(string database, string schema, string partitionName)
    {
        StringBuilder stringBuilder = new();
        await GetCreatePartitionTextStringBuilder(stringBuilder, database, schema, partitionName);
        return stringBuilder.ToString();
    }

    public virtual async ValueTask GetCreatePartitionTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string partitionName)
    {
        await Task.CompletedTask;
    }

    public virtual string GetPostgresIndexPartitionOverview(string database, string schema, string tableName)
    {
        return "not supported";
    }

    public virtual string GetPostgresMaintenanceCommandPack(string database, string schema, string tableName)
    {
        return "not supported";
    }

    public virtual string GetCheckDistributeText(string database, string schema, string tableName)
    {
        return "not implemented yet";
    }

    public virtual string GetKeyCodeText(string database, string schema, string tableName)
    {
        var f = GetQuotedTwoOrTreePartName(database, schema, tableName);
        var d = QuoteNameIfNeeded($"PK_{tableName}");
        return $"ALTER TABLE {f} ADD CONSTRAINT {d} PRIMARY KEY (<COL1>,<COL2>);";
    }

    public virtual string GetKeyUniqueCodeText(string database, string schema, string tableName)
    {
        var f = GetQuotedTwoOrTreePartName(database, schema, tableName);
        var d = QuoteNameIfNeeded($"UK_{tableName}");
        return $"ALTER TABLE {f} ADD CONSTRAINT {d} UNIQUE (<COL1>,<COL2>);";
    }

    public async ValueTask<string> GetCreateViewText(string database, string schema, string tableName)
    {
        var stringBuilder = new StringBuilder();
        await GetCreateViewTextStringBuilder(stringBuilder, database, schema, tableName);
        return stringBuilder.ToString();
    }

    public virtual async ValueTask GetCreateViewTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName)
    {
        await Task.CompletedTask;
    }

    public async ValueTask<string> GetCreateProcedureText(string database, string schema, string procedureName, bool forceFreshCode = false)
    {
        var stringBuilder = new StringBuilder();
        await GetCreateProcedureTextStringBuilder(stringBuilder, database, schema, procedureName, forceFreshCode);
        return stringBuilder.ToString();
    }
    public virtual async ValueTask GetCreateProcedureTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName, bool forceFreshCode = false)
    {
        await Task.CompletedTask;
    }

    public virtual string GetCreateProcedureCall(string database, string schema, string tableName)
    {
        var ext = "";
        if (tableName.EndsWith(')'))
        {
            int index = tableName.LastIndexOf('(');
            ext = tableName[index..]; // ok becouse lst is ')' so index < length-1 for sure
            tableName = tableName[..tableName.IndexOf('(')];
        }

        var f = GetQuotedTwoOrTreePartName(database, schema, tableName);

        return $"CALL PROCEDURE {f}{ext}";
    }

    public virtual (int position, int length) HandleExceptions(ReadOnlySpan<char> sqlText, Exception exception)
    {
        return (-1, -1);
    }

}
