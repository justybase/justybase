using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace JustyBase.Services.Results;

/// <summary>
/// Local SQLite spill for large query results. MVP: write-all, read-only paging.
/// Grouping/filter stay in-memory only when results stay below the spill threshold.
/// </summary>
public sealed class ResultSpillStore : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _writeTx;
    private SqliteCommand? _insertCmd;
    private bool _disposed;
    private int _rowCount;

    public ResultSpillStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(Path.GetTempPath(), "JustyBaseSpill");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, $"spill_{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared;Pooling=False");
        _connection.Open();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE rows (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              payload TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public string DatabasePath => _dbPath;
    public int RowCount => _rowCount;
    public int PageSize { get; set; } = 500;

    public void BeginWriteBatch()
    {
        _writeTx = _connection.BeginTransaction();
        _insertCmd = _connection.CreateCommand();
        _insertCmd.Transaction = _writeTx;
        _insertCmd.CommandText = "INSERT INTO rows (payload) VALUES ($p);";
        var p = _insertCmd.CreateParameter();
        p.ParameterName = "$p";
        _insertCmd.Parameters.Add(p);
    }

    public void WriteRow(object?[] fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_insertCmd is null)
        {
            BeginWriteBatch();
        }

        _insertCmd!.Parameters[0].Value = JsonSerializer.Serialize(
            new SpillRow(Normalize(fields)),
            SpillJsonContext.Default.SpillRow);
        _insertCmd.ExecuteNonQuery();
        _rowCount++;
    }

    public void EndWriteBatch()
    {
        _writeTx?.Commit();
        _writeTx?.Dispose();
        _writeTx = null;
        _insertCmd?.Dispose();
        _insertCmd = null;
    }

    public IReadOnlyList<object?[]> ReadPage(int pageIndex, int pageSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageSize <= 0)
        {
            return [];
        }

        pageIndex = Math.Max(0, pageIndex);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT payload FROM rows ORDER BY id LIMIT $limit OFFSET $offset;";
        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", pageIndex * pageSize);

        var list = new List<object?[]>(pageSize);
        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
        while (reader.Read())
        {
            var json = reader.GetString(0);
            var row = JsonSerializer.Deserialize(json, SpillJsonContext.Default.SpillRow);
            list.Add(Denormalize(row?.Values ?? []));
        }

        return list;
    }

    public int PageCount(int pageSize)
    {
        if (pageSize <= 0 || _rowCount == 0)
        {
            return 0;
        }

        return (_rowCount + pageSize - 1) / pageSize;
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
        catch
        {
            // ignore flush errors on dispose
        }

        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // best-effort cleanup (Windows may briefly keep the handle)
        }
    }

    private static JsonElement[] Normalize(object?[] fields)
    {
        var result = new JsonElement[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            result[i] = ToJsonElement(fields[i]);
        }

        return result;
    }

    private static JsonElement ToJsonElement(object? value)
        => value switch
        {
            null or DBNull => JsonSerializer.SerializeToElement((string?)null, SpillJsonContext.Default.String),
            JsonElement element => element.Clone(),
            string text => JsonSerializer.SerializeToElement(text, SpillJsonContext.Default.String),
            DateTime dateTime => JsonSerializer.SerializeToElement(dateTime.ToString("O", CultureInfo.InvariantCulture), SpillJsonContext.Default.String),
            byte[] bytes => JsonSerializer.SerializeToElement(Convert.ToBase64String(bytes), SpillJsonContext.Default.String),
            bool boolean => JsonSerializer.SerializeToElement(boolean, SpillJsonContext.Default.Boolean),
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => JsonSerializer.SerializeToElement(Convert.ToDecimal(value, CultureInfo.InvariantCulture), SpillJsonContext.Default.Decimal),
            _ => JsonSerializer.SerializeToElement(Convert.ToString(value, CultureInfo.InvariantCulture), SpillJsonContext.Default.String)
        };

    private static object?[] Denormalize(JsonElement[] values)
    {
        // JsonSerializer returns JsonElement for many values; coerce to CLR for grid display.
        var result = new object?[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = FromJsonElement(values[i]);
        }

        return result;
    }

    private static object? FromJsonElement(JsonElement je)
        => je.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number when je.TryGetInt64(out var l) => l,
            JsonValueKind.Number when je.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => je.ToString()
        };
}

internal sealed record SpillRow(JsonElement[] Values);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SpillRow))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(decimal))]
internal partial class SpillJsonContext : JsonSerializerContext
{
}
