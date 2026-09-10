using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using UCanAccess;
using UCanAccess.AccessCrypto;

namespace AccessPlugin;

/// <summary>
/// Microsoft Access database service backed by the pure .NET UCanAccess provider.
/// </summary>
public sealed class Access : DatabaseService, ILoginDataAwareDatabaseService
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.Access;

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, DatabaseColumn[]>>> _columns =
        new(StringComparer.OrdinalIgnoreCase);

    public AccessConnectionOptions ConnectionOptions { get; private set; } = new();

    public Access(string username, string password, string port, string ip, string db, int connectionTimeout)
        : base(username, password, port, ip, db, connectionTimeout)
    {
        DatabaseType = WHO_I_AM_CONST;
        AutoCompletDatabaseMode = CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional;
        preferDatabaseInCodes = false;
        PrefrerUpperCase = false;
    }

    public void ApplyLoginData(LoginDataModel loginData)
    {
        ArgumentNullException.ThrowIfNull(loginData);
        ConnectionOptions = loginData.AccessOptions ?? new AccessConnectionOptions();
    }

    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        string path = ResolveDatabasePath(databaseName);
        string connectionString = BuildConnectionString(path);
        var connection = new UCanAccessConnection(connectionString)
        {
            // The optional crypto package is part of this plugin so plaintext and
            // modern encrypted ACCDB files follow the same provider path.
            DatabaseOpener = new AccessCryptoOpener()
        };
        Connection = connection;
        return connection;
    }

    protected override List<(string databaseName, string defaultSchema)> GetDatabases()
    {
        string path = ResolveDatabasePath(null);
        return [(path, DefaultDatabaseSchema)];
    }

    protected override string DefaultDatabaseSchema => "MAIN";

    protected override string GetSqlTablesAndOtherObjects(string dbName) => string.Empty;

    protected override string GetSqlOfColumns(string dbName) => string.Empty;

    protected override string? GetProceduresSql(string database, string objectFilterName) => null;

    protected override string? GetViewsSql(string database, string objectFilterName)
        => "SELECT NULL AS SCHEMA_, NULL AS VIEW_NAME, NULL AS VIEW_SOURCE WHERE 1 = 0";

    protected override string? GetExternalTableSql(string database) => null;

    protected override string? GetSynonymSql(string database) => null;

    protected override void LoadDatabaseObject(string database, DbConnection con)
    {
        Dictionary<string, Dictionary<string, DatabaseObject>> databaseObjects =
            _databaseSchemaTable[database];
        Dictionary<string, DatabaseObject> mainObjects = [];
        databaseObjects[DefaultDatabaseSchema] = mainObjects;

        using DataTable tables = con.GetSchema("Tables");
        int id = 1;
        foreach (DataRow row in tables.Rows)
        {
            string? name = ReadString(row, "TABLE_NAME");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string tableType = ReadString(row, "TABLE_TYPE") ?? "TABLE";
            bool isView = tableType.Equals("VIEW", StringComparison.OrdinalIgnoreCase);
            TypeInDatabaseEnum objectType = isView ? TypeInDatabaseEnum.View : TypeInDatabaseEnum.Table;
            mainObjects[name] = new DatabaseObject(
                id++,
                name,
                ReadString(row, "DESCRIPTION"),
                objectType,
                isView ? "VIEW" : "TABLE",
                "ACCESS",
                ReadDateTime(row, "DATE_CREATED"));
        }
    }

    protected override void LoadColumns(string database, DbConnection con)
    {
        var databaseColumns = new Dictionary<string, Dictionary<string, DatabaseColumn[]>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [DefaultDatabaseSchema] = new Dictionary<string, DatabaseColumn[]>(StringComparer.OrdinalIgnoreCase)
        };

        using DataTable columns = con.GetSchema("Columns");
        foreach (var group in columns.Rows.Cast<DataRow>()
                     .Select(row => (Row: row, Name: ReadString(row, "TABLE_NAME")))
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase))
        {
            var result = new List<DatabaseColumn>();
            foreach (var item in group.OrderBy(item => ReadInt32(item.Row, "ORDINAL_POSITION")))
            {
                DataRow row = item.Row;
                string columnName = ReadString(row, "COLUMN_NAME") ?? "COLUMN";
                string typeName = ReadString(row, "DATA_TYPE_NAME")
                    ?? ReadString(row, "DATA_TYPE")
                    ?? "TEXT";
                bool notNull = string.Equals(ReadString(row, "IS_NULLABLE"), "NO", StringComparison.OrdinalIgnoreCase);
                result.Add(new DatabaseColumn(
                    columnName,
                    null,
                    typeName,
                    notNull,
                    ReadString(row, "COLUMN_DEFAULT")));
            }

            databaseColumns[DefaultDatabaseSchema][group.Key] = result.ToArray();
        }

        _columns[database] = databaseColumns;
    }

    public override IEnumerable<DatabaseColumn> GetColumns(string? database, string? schema, string? table, string filter)
    {
        if (string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(schema)
            || string.IsNullOrWhiteSpace(table)
            || !_columns.TryGetValue(database, out var databases)
            || !databases.TryGetValue(schema, out var schemas)
            || !schemas.TryGetValue(table, out var columns))
        {
            yield break;
        }

        foreach (DatabaseColumn column in columns)
        {
            if (string.IsNullOrWhiteSpace(filter)
                || column.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                yield return column;
            }
        }
    }

    public override bool IsTypeInDatabaseSupported(TypeInDatabaseEnum typeInDatabase)
        => typeInDatabase is TypeInDatabaseEnum.Table or TypeInDatabaseEnum.View;

    public override void ClearCachedData()
    {
        base.ClearCachedData();
        _columns.Clear();
    }

    public override string QuoteNameIfNeeded(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length >= 2 && word[0] == '[' && word[^1] == ']')
        {
            return word;
        }

        return $"[{word.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public override string GetTableDropCode(string fullName) => $"DROP TABLE {fullName};";

    public override string GetTableRenameCode(string fullName) => $"ALTER TABLE {fullName} RENAME TO [ABC];";

    public override string GetShortSelectCode(string fullName) => $"SELECT TOP 100 * FROM {fullName};";

    public override string GetCreateFromCode(string fullName)
        => $"SELECT TOP 100 * INTO [ABC] FROM {fullName};";

    public override string GetTop100Select(
        string database,
        string schema,
        string table,
        bool snippetMode,
        bool addWhereToTextCols = false)
    {
        // Access exposes user tables without a database/schema qualifier. The
        // synthetic MAIN schema used by the schema tree is an IDE grouping only.
        string tableName = QuoteNameIfNeeded(table);
        return $"SELECT TOP 100 * FROM {tableName};";
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
        string targetName = QuoteNameIfNeeded(overrideTableName ?? tableName);
        DatabaseColumn[] columns = GetColumns(database, schema, tableName, "").ToArray();
        sb.Append("CREATE TABLE ").Append(targetName).AppendLine(" (");
        if (columns.Length == 0)
        {
            sb.AppendLine("    [ID] LONG");
        }
        else
        {
            for (int i = 0; i < columns.Length; i++)
            {
                DatabaseColumn column = columns[i];
                if (i > 0)
                {
                    sb.AppendLine(",");
                }

                sb.Append("    ")
                    .Append(QuoteNameIfNeeded(column.Name))
                    .Append(' ')
                    .Append(string.IsNullOrWhiteSpace(column.FullTypeName) ? "TEXT" : column.FullTypeName);
                if (column.ColumnNotNull)
                {
                    sb.Append(" NOT NULL");
                }
            }
            sb.AppendLine();
        }

        sb.Append(");");
        await ValueTask.CompletedTask;
    }

    private string ResolveDatabasePath(string? databaseName)
    {
        string path = string.IsNullOrWhiteSpace(databaseName) ? Database : databaseName;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("An Access database file path is required.");
        }

        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".mdb", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".accdb", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Access connections require an .mdb or .accdb file.", nameof(databaseName));
        }

        return fullPath;
    }

    private string BuildConnectionString(string path)
    {
        var values = new List<string>
        {
            $"Data Source={QuoteConnectionValue(path)}",
            $"Read Only={ConnectionOptions.ReadOnly.ToString().ToLowerInvariant()}",
            $"Show Schema={ConnectionOptions.ShowSchema.ToString().ToLowerInvariant()}",
            $"Allow External Links={ConnectionOptions.AllowExternalLinks.ToString().ToLowerInvariant()}",
            $"Lazy Load={ConnectionOptions.LazyLoad.ToString().ToLowerInvariant()}",
            $"Keep Mirror=true",
            $"Mirror Mode={QuoteConnectionValue(string.IsNullOrWhiteSpace(ConnectionOptions.MirrorMode) ? "memory" : ConnectionOptions.MirrorMode)}"
        };

        if (!string.IsNullOrEmpty(Password))
        {
            values.Add($"Password={QuoteConnectionValue(Password)}");
        }

        return string.Join(';', values);
    }

    private static string QuoteConnectionValue(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string? ReadString(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) || row[columnName] is DBNull)
        {
            return null;
        }

        string value = Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
        return value.Length == 0 ? null : value;
    }

    private static int ReadInt32(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) || row[columnName] is DBNull)
        {
            return 0;
        }

        return Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);
    }

    private static DateTime? ReadDateTime(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) || row[columnName] is DBNull)
        {
            return null;
        }

        return row[columnName] is DateTime dateTime ? dateTime : null;
    }
}
