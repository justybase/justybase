using DuckDB.NET.Data;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ExcelPlugin;

/// <summary>
/// SQL façade for a single XLSX/XLSB workbook. Sheets are materialized into a
/// private DuckDB file so every ADO.NET caller receives an independent connection.
/// </summary>
public sealed class Excel : DatabaseService
{
    public override DatabaseTypeEnum DatabaseType => DatabaseTypeEnum.Excel;

    private readonly Lock _materializationLock = new();
    private string? _sourcePath;
    private string? _materializedPath;
    private long _sourceLength = -1;
    private long _sourceWriteTicks;

    public Excel(DbConnectionOptions options)
        : base(options)
    {
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional;
        preferDatabaseInCodes = false;
        PrefrerUpperCase = false;
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        string sourcePath = ResolveSourcePath(databaseName);
        string materializedPath = EnsureMaterialized(sourcePath);
        return new DuckDBConnection($"Data Source={materializedPath}");
    }

    protected override List<(string databaseName, string defaultSchema)> GetDatabases()
    {
        string sourcePath = ResolveSourcePath(null);
        _ = EnsureMaterialized(sourcePath);
        return [(sourcePath, "main")];
    }

    protected override string GetSqlTablesAndOtherObjects(string dbName)
        => """
           SELECT
               CAST(table_oid AS INTEGER),
               table_name,
               comment,
               schema_name,
               'TABLE',
               '',
               NULL
           FROM duckdb_tables()
           WHERE schema_name = 'main'
             AND NOT internal

           UNION ALL

           SELECT
               CAST(view_oid AS INTEGER),
               view_name,
               comment,
               schema_name,
               'VIEW',
               '',
               NULL
           FROM duckdb_views()
           WHERE schema_name = 'main'
             AND NOT internal

           ORDER BY 2
           """;

    protected override string GetSqlOfColumns(string dbName)
        => """
           SELECT
               CAST(table_oid AS INTEGER) AS OBJECT_ID,
               column_name,
               NULL AS DESCRIPTION,
               data_type ||
                   COALESCE('(' || character_maximum_length || ')',
                            '(' || numeric_precision || ',' || numeric_scale || ')', '') ||
                   CASE WHEN NOT is_nullable THEN ' NOT NULL' ELSE '' END,
               CASE WHEN NOT is_nullable THEN 1 ELSE 0 END,
               column_default
           FROM duckdb_columns()
           WHERE schema_name = 'main'
             AND NOT internal
           ORDER BY table_oid, column_index
           """;

    protected override string? GetProceduresSql(string database, string objectFilterName) => null;

    protected override string? GetViewsSql(string database, string objectFilterName)
        => "SELECT NULL AS SCHEMA_, NULL AS VIEW_NAME, NULL AS VIEW_SOURCE WHERE 1 = 0";

    protected override string? GetExternalTableSql(string database) => null;

    protected override string? GetSynonymSql(string database) => null;

    public override bool IsTypeInDatabaseSupported(TypeInDatabaseEnum typeInDatabase)
        => typeInDatabase is TypeInDatabaseEnum.Table or TypeInDatabaseEnum.View;

    public override void ClearCachedData()
    {
        base.ClearCachedData();
        lock (_materializationLock)
        {
            TryDelete(_materializedPath);
            _materializedPath = null;
            _sourcePath = null;
            _sourceLength = -1;
            _sourceWriteTicks = 0;
        }
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
        string sourcePath = ResolveSourcePath(database);
        string materializedPath = EnsureMaterialized(sourcePath);
        await using var connection = new DuckDBConnection($"Data Source={materializedPath}");
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM duckdb_tables() WHERE schema_name = 'main' AND table_name = ?";
        var parameter = command.CreateParameter();
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        object? sql = await command.ExecuteScalarAsync().ConfigureAwait(false);
        sb.Append(Convert.ToString(sql, CultureInfo.InvariantCulture) ?? $"-- No definition for {tableName}");
    }

    private string ResolveSourcePath(string? databaseName)
    {
        string path = string.IsNullOrWhiteSpace(databaseName) ? Database : databaseName;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("An Excel workbook path is required.");
        }

        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".xlsb", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Excel connections require an .xlsx or .xlsb file.", nameof(databaseName));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Excel workbook was not found.", fullPath);
        }

        return fullPath;
    }

    private string EnsureMaterialized(string sourcePath)
    {
        FileInfo sourceInfo = new(sourcePath);
        lock (_materializationLock)
        {
            if (_materializedPath is not null
                && string.Equals(_sourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                && _sourceLength == sourceInfo.Length
                && _sourceWriteTicks == sourceInfo.LastWriteTimeUtc.Ticks
                && File.Exists(_materializedPath))
            {
                return _materializedPath;
            }

            string oldMaterializedPath = _materializedPath ?? string.Empty;
            string directory = string.IsNullOrWhiteSpace(TempDataDirectory)
                ? Path.GetTempPath()
                : TempDataDirectory;
            Directory.CreateDirectory(directory);
            string sourceHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))
                .ToLowerInvariant();
            string materializedPath = Path.Combine(
                directory,
                $"justybase-excel-{sourceHash}-{Guid.NewGuid():N}.duckdb");

            TryDelete(materializedPath);
            using (var connection = new DuckDBConnection($"Data Source={materializedPath}"))
            {
                connection.Open();
                try
                {
                    LoadWorkbook(connection, sourcePath);
                }
                finally
                {
                    connection.Close();
                }
            }

            if (!string.Equals(oldMaterializedPath, materializedPath, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(oldMaterializedPath);
            }

            _sourcePath = sourcePath;
            _sourceLength = sourceInfo.Length;
            _sourceWriteTicks = sourceInfo.LastWriteTimeUtc.Ticks;
            _materializedPath = materializedPath;
            return materializedPath;
        }
    }

    private void LoadWorkbook(DuckDBConnection connection, string sourcePath)
    {
        var factory = new SpreadSheetSourceFactory();
        string[] sheetNames;
        using (IImportSource namesSource = factory.OpenSource(sourcePath, null))
        {
            sheetNames = namesSource.GetSheetNames().ToArray();
        }

        if (sheetNames.Length == 0)
        {
            throw new InvalidDataException("The Excel workbook does not contain any worksheets.");
        }

        foreach (string sheetName in sheetNames)
        {
            SheetScanResult scan;
            using (IImportSource scanSource = factory.OpenSource(sourcePath, null))
            {
                scan = TabularImportScanner.ScanSource(scanSource, sheetName);
            }

            string[] headers = MakeUniqueHeaders(scan.NormalizedHeaders);
            if (headers.Length == 0)
            {
                continue;
            }

            CreateSheetTable(connection, sheetName, headers, scan.DetectedTypes);

            using IImportSource dataSource = factory.OpenSource(sourcePath, null);
            dataSource.ActualSheetName = sheetName;
            if (!dataSource.Read())
            {
                continue;
            }

            ImportColumnKind[] kinds = scan.DetectedTypes.Select(type => type.Kind).ToArray();
            using IDataReader reader = dataSource.CreateTypedReader(kinds, headers);
            InsertRows(connection, sheetName, headers, reader);
        }
    }

    private static void CreateSheetTable(
        DuckDBConnection connection,
        string sheetName,
        IReadOnlyList<string> headers,
        IReadOnlyList<DetectedImportColumnType> types)
    {
        string columns = string.Join(", ", headers.Select((header, index) =>
            $"{QuoteIdentifier(header)} {DuckDbType(types[index].Kind)}"));
        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {QuoteIdentifier(sheetName)} ({columns});";
        command.ExecuteNonQuery();
    }

    private static void InsertRows(
        DuckDBConnection connection,
        string sheetName,
        IReadOnlyList<string> headers,
        IDataReader reader)
    {
        string table = QuoteIdentifier(sheetName);
        string columnList = string.Join(", ", headers.Select(QuoteIdentifier));
        string parameters = string.Join(", ", Enumerable.Range(0, headers.Count).Select(static _ => "?"));
        using DbTransaction transaction = connection.BeginTransaction();
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table} ({columnList}) VALUES ({parameters});";
        for (int i = 0; i < headers.Count; i++)
        {
            DbParameter parameter = command.CreateParameter();
            command.Parameters.Add(parameter);
        }

        while (reader.Read())
        {
            for (int i = 0; i < headers.Count; i++)
            {
                command.Parameters[i].Value = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static string[] MakeUniqueHeaders(IReadOnlyList<string> headers)
    {
        var result = new string[headers.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            string baseName = string.IsNullOrWhiteSpace(headers[i]) ? $"Column{i + 1}" : headers[i].Trim();
            string candidate = baseName;
            int suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{baseName}_{suffix++}";
            }

            result[i] = candidate;
        }

        return result;
    }

    private static string DuckDbType(ImportColumnKind kind) => kind switch
    {
        ImportColumnKind.Integer => "BIGINT",
        ImportColumnKind.Numeric => "DECIMAL(38, 10)",
        ImportColumnKind.Date => "DATE",
        ImportColumnKind.TimeStamp => "TIMESTAMP",
        ImportColumnKind.Boolean => "BOOLEAN",
        _ => "VARCHAR"
    };

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A live query may still hold the old materialization. It is safe to
            // leave that temporary file for the OS/user cleanup path.
        }
    }
}
