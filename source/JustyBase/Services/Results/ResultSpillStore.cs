using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace JustyBase.Services.Results;

/// <summary>
/// Local SQLite spill for large query results. Columns are typed at creation
/// time (integer/real/text/blob, with an exact-text plus REAL sort pair for
/// decimals), so paging, sorting and filtering run in SQLite instead of
/// re-parsing one JSON payload per row.
///
/// The store is append-only while a query runs. After <see cref="EndWriteBatch"/>
/// it is read-only and safe to page from the UI thread. When the shared spill
/// quota is exhausted, <see cref="WriteRow"/> throws
/// <see cref="SpillQuotaExceededException"/> and the caller keeps the rows that
/// were already persisted.
/// </summary>
public sealed class ResultSpillStore : IDisposable
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";
    private const int MaterializePageSize = 10_000;

    private readonly ResultSpillSchema _schema;
    private readonly SpillStoreManager _manager;
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly string[] _valueColumns;
    private readonly string?[] _sortColumns;
    private readonly string[] _sqliteTypes;
    private readonly string _selectList;
    private readonly string _storeId = Guid.NewGuid().ToString("N");
    private readonly Lock _gate = new();

    private SqliteTransaction? _writeTx;
    private SqliteCommand? _insertCmd;
    private bool _disposed;
    private int _rowCount;
    private int _filteredRowCount;
    private long _reservedBytes;
    private long _version;
    private SpillFilter? _activeFilter;
    private string _whereSql = string.Empty;
    private List<(string Name, object? Value)> _filterParameters = [];

    internal ResultSpillStore(ResultSpillSchema schema, SpillStoreManager manager, string directory)
    {
        _schema = schema;
        _manager = manager;

        _valueColumns = new string[schema.Columns.Count];
        _sortColumns = new string?[schema.Columns.Count];
        _sqliteTypes = new string[schema.Columns.Count];

        var select = new StringBuilder();
        for (int i = 0; i < schema.Columns.Count; i++)
        {
            ResultSpillColumn column = schema.Columns[i];
            _valueColumns[i] = $"c{i}";
            _sortColumns[i] = column.NeedsSortColumn ? $"c{i}_sort" : null;
            _sqliteTypes[i] = column.Kind switch
            {
                SpillColumnKind.Integral or SpillColumnKind.Boolean => "INTEGER",
                SpillColumnKind.Real => "REAL",
                SpillColumnKind.Blob => "BLOB",
                _ => "TEXT"
            };

            if (i > 0)
            {
                select.Append(',');
            }

            select.Append(_valueColumns[i]);
        }

        _selectList = select.ToString();

        _dbPath = Path.Combine(directory, $"spill_{Environment.ProcessId}_{_storeId}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared;Pooling=False");
        _connection.Open();
        CreateSchema();
    }

    public string DatabasePath => _dbPath;

    public int RowCount => _rowCount;

    public int FilteredRowCount
    {
        get
        {
            lock (_gate)
            {
                return _activeFilter is null ? _rowCount : _filteredRowCount;
            }
        }
    }

    /// <summary>Estimated managed bytes represented by the persisted rows.</summary>
    public long EstimatedBytes => Interlocked.Read(ref _reservedBytes);

    public int PageSize { get; set; } = 500;

    internal ResultSpillSchema Schema => _schema;

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "SQL text is composed only from generated column identifiers (c0..cN); every value is a bound parameter.")]
    public void BeginWriteBatch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_writeTx is not null)
            {
                return;
            }

            _writeTx = _connection.BeginTransaction();
            _insertCmd = _connection.CreateCommand();
            _insertCmd.Transaction = _writeTx;

            var sql = new StringBuilder("INSERT INTO rows (");
            var values = new StringBuilder();
            int parameterIndex = 0;
            for (int i = 0; i < _valueColumns.Length; i++)
            {
                if (i > 0)
                {
                    sql.Append(',');
                    values.Append(',');
                }

                sql.Append(_valueColumns[i]);
                values.Append("$p").Append(parameterIndex++);

                if (_sortColumns[i] is not null)
                {
                    sql.Append(',').Append(_sortColumns[i]);
                    values.Append(",$p").Append(parameterIndex++);
                }
            }

            sql.Append(") VALUES (").Append(values).Append(");");
            _insertCmd.CommandText = sql.ToString();

            parameterIndex = 0;
            for (int i = 0; i < _valueColumns.Length; i++)
            {
                _insertCmd.Parameters.Add(CreateParameter($"$p{parameterIndex++}", i, sort: false));
                if (_sortColumns[i] is not null)
                {
                    _insertCmd.Parameters.Add(CreateParameter($"$p{parameterIndex++}", i, sort: true));
                }
            }
        }
    }

    public void WriteRow(object?[] fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Length != _schema.Columns.Count)
        {
            throw new ArgumentException(
                $"Expected {_schema.Columns.Count} fields, received {fields.Length}.",
                nameof(fields));
        }

        if (_insertCmd is null)
        {
            BeginWriteBatch();
        }

        long estimate = ResultRowSizer.Estimate(fields);
        if (!_manager.TryReserve(estimate))
        {
            throw new SpillQuotaExceededException(
                _manager.QuotaBytes,
                _manager.UsedBytes,
                estimate);
        }

        try
        {
            lock (_gate)
            {
                int parameterIndex = 0;
                for (int i = 0; i < fields.Length; i++)
                {
                    SqliteParameter valueParameter = _insertCmd!.Parameters[parameterIndex++];
                    valueParameter.Value = ToStorageValue(fields[i], _schema.Columns[i]);

                    if (_sortColumns[i] is not null)
                    {
                        SqliteParameter sortParameter = _insertCmd.Parameters[parameterIndex++];
                        sortParameter.Value = ToSortValue(fields[i]);
                    }
                }

                _insertCmd!.ExecuteNonQuery();
                _rowCount++;
                _filteredRowCount = _rowCount;
                Interlocked.Add(ref _reservedBytes, estimate);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _manager.Release(estimate);
            throw;
        }
    }

    public void EndWriteBatch()
    {
        lock (_gate)
        {
            _writeTx?.Commit();
            _writeTx?.Dispose();
            _writeTx = null;
            _insertCmd?.Dispose();
            _insertCmd = null;

            _activeFilter = null;
            _whereSql = string.Empty;
            _filterParameters = [];
            _filteredRowCount = _rowCount;
            InvalidateCacheLocked();
        }
    }

    /// <summary>
    /// Replaces the active row filter and returns the number of matching rows.
    /// Passing an empty filter restores all rows.
    /// </summary>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "SQL text is composed only from generated column identifiers (c0..cN); all values are bound parameters.")]
    public int SetFilter(SpillFilter? filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (filter is null || filter.IsEmpty)
            {
                _activeFilter = null;
                _whereSql = string.Empty;
                _filterParameters = [];
                _filteredRowCount = _rowCount;
                InvalidateCacheLocked();
                return _rowCount;
            }

            (string whereSql, List<(string Name, object? Value)> parameters) = BuildWhere(filter);
            _activeFilter = filter;
            _whereSql = whereSql;
            _filterParameters = parameters;

            using SqliteCommand countCommand = _connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM rows" + _whereSql + ";";
            AddFilterParameters(countCommand, _filterParameters);
            _filteredRowCount = Convert.ToInt32(
                countCommand.ExecuteScalar() ?? 0,
                CultureInfo.InvariantCulture);
            InvalidateCacheLocked();
            return _filteredRowCount;
        }
    }

    public IReadOnlyList<object?[]> ReadPage(int pageIndex, int pageSize)
        => ReadPage(pageIndex, pageSize, sortKeys: null);

    /// <summary>
    /// Reads a page with an explicit filter snapshot instead of the store's
    /// current filter. Export and clipboard readers use this so a user changing
    /// the grid filter mid-export cannot mix two result sets in one file.
    /// </summary>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "SQL text is composed only from generated column identifiers (c0..cN); all values are bound parameters.")]
    internal IReadOnlyList<object?[]> ReadPageWithFilter(
        int pageIndex,
        int pageSize,
        IReadOnlyList<SpillSortKey>? sortKeys,
        SpillFilter? filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageSize <= 0)
        {
            return [];
        }

        pageIndex = Math.Max(0, pageIndex);

        lock (_gate)
        {
            (string whereSql, List<(string Name, object? Value)> parameters) =
                filter is null || filter.IsEmpty
                    ? (string.Empty, [])
                    : BuildWhere(filter);

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                $"SELECT {_selectList} FROM rows{whereSql} ORDER BY {BuildOrderBy(sortKeys)} LIMIT $limit OFFSET $offset;";
            AddFilterParameters(command, parameters);
            command.Parameters.AddWithValue("$limit", pageSize);
            command.Parameters.AddWithValue("$offset", (long)pageIndex * pageSize);

            var rows = new List<object?[]>(pageSize);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(ReadRow(reader));
            }

            return rows;
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "SQL text is composed only from generated column identifiers (c0..cN); all values are bound parameters.")]
    public IReadOnlyList<object?[]> ReadPage(
        int pageIndex,
        int pageSize,
        IReadOnlyList<SpillSortKey>? sortKeys)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageSize <= 0)
        {
            return [];
        }

        pageIndex = Math.Max(0, pageIndex);
        string sortSignature = BuildSortSignature(sortKeys);
        var cacheKey = new SpillPageKey(StoreId(), Version(), pageIndex, pageSize, sortSignature);

        if (_manager.Cache.TryGet(cacheKey, out object?[][] cached))
        {
            return cached;
        }

        lock (_gate)
        {
            int total = _activeFilter is null ? _rowCount : _filteredRowCount;
            if ((long)pageIndex * pageSize >= total)
            {
                return [];
            }

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                $"SELECT {_selectList} FROM rows{_whereSql} ORDER BY {BuildOrderBy(sortKeys)} LIMIT $limit OFFSET $offset;";
            AddFilterParameters(command, _filterParameters);
            command.Parameters.AddWithValue("$limit", pageSize);
            command.Parameters.AddWithValue("$offset", (long)pageIndex * pageSize);

            var rows = new List<object?[]>(pageSize);
            long bytes = 0;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    object?[] row = ReadRow(reader);
                    rows.Add(row);
                    bytes += ResultRowSizer.Estimate(row);
                }
            }

            object?[][] result = [.. rows];
            _manager.Cache.Set(cacheKey, result, bytes);
            return result;
        }
    }

    public IReadOnlyList<object?[]> ReadAll(IReadOnlyList<SpillSortKey>? sortKeys = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int total = FilteredRowCount;
        var rows = new List<object?[]>(total);
        for (int page = 0; (long)page * MaterializePageSize < total; page++)
        {
            IReadOnlyList<object?[]> pageRows = ReadPage(page, MaterializePageSize, sortKeys);
            if (pageRows.Count == 0)
            {
                break;
            }

            rows.AddRange(pageRows);
        }

        return rows;
    }

    public int PageCount(int pageSize)
    {
        int total = FilteredRowCount;
        if (pageSize <= 0 || total == 0)
        {
            return 0;
        }

        return (total + pageSize - 1) / pageSize;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            EndWriteBatch();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            // Ignore flush errors on dispose.
        }

        _manager.Cache.RemoveStore(StoreId());
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        DeleteSpillFile(_dbPath);
        DeleteSpillFile(_dbPath + "-wal");
        DeleteSpillFile(_dbPath + "-shm");

        _manager.Release(Interlocked.Read(ref _reservedBytes));
        Interlocked.Exchange(ref _reservedBytes, 0);
        _manager.RemoveStore(this);
    }

    private static void DeleteSpillFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup; orphan cleanup removes the rest next run.
        }
    }

    private string StoreId() => _storeId;

    private long Version()
    {
        lock (_gate)
        {
            return _version;
        }
    }

    private void InvalidateCacheLocked()
    {
        _version++;
        _manager.Cache.RemoveStore(_storeId);
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "SQL text is composed only from generated column identifiers (c0..cN) and fixed SQLite type names.")]
    private void CreateSchema()
    {
        var sql = new StringBuilder("CREATE TABLE rows (id INTEGER PRIMARY KEY AUTOINCREMENT");
        for (int i = 0; i < _valueColumns.Length; i++)
        {
            sql.Append(',').Append(_valueColumns[i]).Append(' ').Append(_sqliteTypes[i]);
            if (_sortColumns[i] is not null)
            {
                sql.Append(',').Append(_sortColumns[i]).Append(" REAL");
            }
        }

        sql.Append(");");

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql.ToString();
        command.ExecuteNonQuery();
    }

    private SqliteParameter CreateParameter(string name, int columnIndex, bool sort)
    {
        ResultSpillColumn column = _schema.Columns[columnIndex];
        SqliteType type = sort
            ? SqliteType.Real
            : column.Kind switch
            {
                SpillColumnKind.Integral or SpillColumnKind.Boolean => SqliteType.Integer,
                SpillColumnKind.Real => SqliteType.Real,
                SpillColumnKind.Blob => SqliteType.Blob,
                _ => SqliteType.Text
            };

        return new SqliteParameter(name, type);
    }

    private static object ToStorageValue(object? value, ResultSpillColumn column)
    {
        if (value is null or DBNull)
        {
            return DBNull.Value;
        }

        return column.Kind switch
        {
            SpillColumnKind.Integral or SpillColumnKind.Boolean => ToInt64(value),
            SpillColumnKind.Real => ToDouble(value),
            SpillColumnKind.Numeric => ToDecimal(value).ToString("G29", CultureInfo.InvariantCulture),
            SpillColumnKind.DateTime => ToDateTimeText(value),
            SpillColumnKind.Blob => ToBytes(value),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static object ToSortValue(object? value)
    {
        if (value is null or DBNull)
        {
            return DBNull.Value;
        }

        try
        {
            return ToDouble(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return DBNull.Value;
        }
    }

    private object?[] ReadRow(SqliteDataReader reader)
    {
        var row = new object?[_schema.Columns.Count];
        for (int i = 0; i < row.Length; i++)
        {
            if (reader.IsDBNull(i))
            {
                row[i] = null;
                continue;
            }

            object raw = reader.GetValue(i);
            row[i] = FromStorageValue(raw, _schema.Columns[i]);
        }

        return row;
    }

    private static object? FromStorageValue(object raw, ResultSpillColumn column)
    {
        switch (column.Kind)
        {
            case SpillColumnKind.Integral:
                return ConvertIntegral(ToInt64(raw), column.TypeCode);
            case SpillColumnKind.Boolean:
                return ToInt64(raw) != 0;
            case SpillColumnKind.Real:
                double real = ToDouble(raw);
                return column.TypeCode == TypeCode.Single ? (float)real : real;
            case SpillColumnKind.Numeric:
                decimal exact = decimal.Parse(
                    Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "0",
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture);
                return column.TypeCode == TypeCode.UInt64 ? decimal.ToUInt64(exact) : exact;
            case SpillColumnKind.DateTime:
                return DateTime.ParseExact(
                    Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty,
                    DateTimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None);
            case SpillColumnKind.Blob:
                return ToBytes(raw);
            default:
                string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                return column.TypeCode == TypeCode.Char && text.Length > 0 ? text[0] : text;
        }
    }

    private static object ConvertIntegral(long value, TypeCode typeCode)
        => typeCode switch
        {
            TypeCode.Byte => (object)(byte)value,
            TypeCode.SByte => (object)(sbyte)value,
            TypeCode.Int16 => (object)(short)value,
            TypeCode.UInt16 => (object)(ushort)value,
            TypeCode.Int32 => (object)(int)value,
            TypeCode.UInt32 => (object)(uint)value,
            _ => (object)value
        };

    private static long ToInt64(object value)
    {
        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static double ToDouble(object value)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static decimal ToDecimal(object value)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0m;
        }
    }

    private static string ToDateTimeText(object value)
    {
        DateTime dateTime = value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            _ => DateTime.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed) ? parsed : default
        };

        return dateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
    }

    private static byte[] ToBytes(object value)
        => value switch
        {
            byte[] bytes => bytes,
            ReadOnlyMemory<byte> memory => memory.ToArray(),
            _ => Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };

    private string BuildOrderBy(IReadOnlyList<SpillSortKey>? sortKeys)
    {
        if (sortKeys is null || sortKeys.Count == 0)
        {
            return "id ASC";
        }

        var clauses = new List<string>(sortKeys.Count + 1);
        foreach (SpillSortKey key in sortKeys)
        {
            if (key.ColumnIndex < 0 || key.ColumnIndex >= _valueColumns.Length)
            {
                continue;
            }

            string column = _sortColumns[key.ColumnIndex] ?? _valueColumns[key.ColumnIndex];
            clauses.Add($"{column} {(key.Descending ? "DESC" : "ASC")}");
        }

        // Stable tie-break so paging never repeats or skips rows with equal keys.
        clauses.Add("id ASC");
        return string.Join(", ", clauses);
    }

    private static string BuildSortSignature(IReadOnlyList<SpillSortKey>? sortKeys)
    {
        if (sortKeys is null || sortKeys.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(',', sortKeys.Select(key => $"{key.ColumnIndex}{(key.Descending ? 'd' : 'a')}"));
    }

    private (string WhereSql, List<(string Name, object? Value)> Parameters) BuildWhere(SpillFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            string search = filter.SearchText;
            var searchClauses = new List<string>();
            for (int i = 0; i < _schema.Columns.Count; i++)
            {
                if (_schema.Columns[i].Kind == SpillColumnKind.Blob)
                {
                    continue;
                }

                string parameter = AddParameter(parameters, filter.ContainsSearch
                    ? "%" + EscapeLike(search) + "%"
                    : EscapeLike(search));
                searchClauses.Add(filter.ContainsSearch
                    ? $"CAST({_valueColumns[i]} AS TEXT) LIKE {parameter} ESCAPE '\\' COLLATE NOCASE"
                    : $"CAST({_valueColumns[i]} AS TEXT) = {parameter} COLLATE NOCASE");
            }

            clauses.Add(searchClauses.Count == 0 ? "1 = 0" : "(" + string.Join(" OR ", searchClauses) + ")");
        }

        foreach (SpillColumnFilter columnFilter in filter.ColumnFilters)
        {
            if (columnFilter.ColumnIndex < 0 || columnFilter.ColumnIndex >= _schema.Columns.Count)
            {
                continue;
            }

            ResultSpillColumn column = _schema.Columns[columnFilter.ColumnIndex];
            string valueColumn = _valueColumns[columnFilter.ColumnIndex];
            string sortColumn = _sortColumns[columnFilter.ColumnIndex] ?? valueColumn;

            switch (columnFilter.Operator)
            {
                case SpillFilterOperator.IsNull:
                    clauses.Add($"{valueColumn} IS NULL");
                    break;
                case SpillFilterOperator.IsNotNull:
                    clauses.Add($"{valueColumn} IS NOT NULL");
                    break;
                case SpillFilterOperator.Equal when columnFilter.Value is null:
                    clauses.Add($"{valueColumn} IS NULL");
                    break;
                case SpillFilterOperator.NotEqual when columnFilter.Value is null:
                    clauses.Add($"{valueColumn} IS NOT NULL");
                    break;
                case SpillFilterOperator.Contains:
                case SpillFilterOperator.StartsWith:
                case SpillFilterOperator.EndsWith:
                    if (column.Kind == SpillColumnKind.Blob)
                    {
                        clauses.Add("1 = 0");
                        break;
                    }

                    string text = EscapeLike(Convert.ToString(columnFilter.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                    string pattern = columnFilter.Operator switch
                    {
                        SpillFilterOperator.Contains => "%" + text + "%",
                        SpillFilterOperator.StartsWith => text + "%",
                        _ => "%" + text
                    };
                    clauses.Add($"CAST({valueColumn} AS TEXT) LIKE {AddParameter(parameters, pattern)} ESCAPE '\\' COLLATE NOCASE");
                    break;
                case SpillFilterOperator.In:
                    var nonNullValues = columnFilter.Values?.Where(value => value is not null).ToList() ?? [];
                    if (nonNullValues.Count == 0)
                    {
                        clauses.Add(columnFilter.IncludeNull ? $"{valueColumn} IS NULL" : "1 = 0");
                        break;
                    }

                    var inParameters = new List<string>(nonNullValues.Count);
                    foreach (object? value in nonNullValues)
                    {
                        inParameters.Add(AddParameter(parameters, ToStorageValue(value, column)));
                    }

                    string inClause = $"{valueColumn} IN ({string.Join(", ", inParameters)})";
                    clauses.Add(columnFilter.IncludeNull
                        ? $"({inClause} OR {valueColumn} IS NULL)"
                        : inClause);
                    break;
                default:
                    string comparisonColumn = column.Kind == SpillColumnKind.Numeric
                        && columnFilter.Operator is not (SpillFilterOperator.Equal or SpillFilterOperator.NotEqual)
                            ? sortColumn
                            : valueColumn;
                    object? boundValue = column.Kind == SpillColumnKind.Numeric
                        ? columnFilter.Operator is SpillFilterOperator.Equal or SpillFilterOperator.NotEqual
                            ? ToStorageValue(columnFilter.Value, column)
                            : ToSortValue(columnFilter.Value)
                        : ToStorageValue(columnFilter.Value, column);
                    clauses.Add($"{comparisonColumn} {OperatorSql(columnFilter.Operator)} {AddParameter(parameters, boundValue)}");
                    break;
            }
        }

        if (clauses.Count == 0)
        {
            return (string.Empty, parameters);
        }

        return (" WHERE " + string.Join(" AND ", clauses), parameters);
    }

    private static string AddParameter(List<(string Name, object? Value)> parameters, object? value)
    {
        string name = "$f" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters.Add((name, value));
        return name;
    }

    private static void AddFilterParameters(SqliteCommand command, List<(string Name, object? Value)> parameters)
    {
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static string OperatorSql(SpillFilterOperator op)
        => op switch
        {
            SpillFilterOperator.Equal => "=",
            SpillFilterOperator.NotEqual => "<>",
            SpillFilterOperator.LessThan => "<",
            SpillFilterOperator.LessThanOrEqual => "<=",
            SpillFilterOperator.GreaterThan => ">",
            SpillFilterOperator.GreaterThanOrEqual => ">=",
            _ => "="
        };

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
