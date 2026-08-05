using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.PluginDatabaseBase.Models;
using System.Data;
using System.Data.Common;

namespace JustyBase.Tests;

/// <summary>
/// Concurrent CacheAllObjects + schema reads must complete without deadlock/hang.
/// </summary>
public sealed class DatabaseCacheManagerConcurrencyTests
{
    [Fact]
    public async Task CacheAllObjects_ParallelCalls_CompleteWithinTimeout()
    {
        var procedureCache = new Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>>();
        var viewCache = new Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>>();
        var synonymCache = new Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>>();
        var schemaTable = new Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>>();
        var defSchema = new Dictionary<string, string>();

        var manager = new DatabaseCacheManager(schemaTable, defSchema, procedureCache, viewCache, synonymCache);

        var openCount = 0;
        Task BuildOnce() => manager.CacheAllObjects(
            [TypeInDatabaseEnum.Procedure, TypeInDatabaseEnum.View],
            databaseName: "",
            procedureName: "",
            getDatabases: _ => ["db1", "db2"],
            getConnection: (_, _) => new FakeDbConnection(() => Interlocked.Increment(ref openCount)),
            createCommandFromConnection: con => con.CreateCommand(),
            getObjectCode: (type, db, _) => type == TypeInDatabaseEnum.Procedure
                ? "PROC"
                : "VIEW",
            isTypeInDatabaseSupported: _ => true,
            netezza: null,
            logger: null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tasks = Enumerable.Range(0, 8).Select(_ => BuildOnce()).ToArray();
        var completed = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(completed, Task.Delay(Timeout.Infinite, cts.Token));

        Assert.Same(completed, finished);
        await completed;

        Assert.True(openCount >= 2);
        Assert.True(procedureCache.Count >= 1 || viewCache.Count >= 1);
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private readonly Action _onOpen;
        private ConnectionState _state = ConnectionState.Closed;

        public FakeDbConnection(Action onOpen) => _onOpen = onOpen;

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "db";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open()
        {
            _onOpen();
            _state = ConnectionState.Open;
            Thread.Sleep(5);
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;

        public FakeDbCommand(FakeDbConnection connection) => _connection = connection;

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get => _connection; set { } }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            if (string.Equals(CommandText, "PROC", StringComparison.Ordinal))
            {
                return new FakeProcReader();
            }

            return new FakeViewReader();
        }
    }

    private sealed class FakeProcReader : DbDataReader
    {
        private int _row = -1;
        public override bool Read() => ++_row < 2;
        public override int FieldCount => 9;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override bool HasRows => true;
        public override bool NextResult() => false;
        public override int GetOrdinal(string name) => 0;
        public override string GetName(int ordinal) => ordinal.ToString();
        public override string GetDataTypeName(int ordinal) => "string";
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override int GetValues(object[] values) => 0;
        public override bool IsDBNull(int ordinal) => false;
        public override bool GetBoolean(int ordinal) => false;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => '\0';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 100 + _row;
        public override long GetInt64(int ordinal) => 0;
        public override float GetFloat(int ordinal) => 0;
        public override double GetDouble(int ordinal) => 0;
        public override string GetString(int ordinal) => ordinal switch
        {
            0 => "SCHEMA",
            6 => $"proc_{_row}()",
            _ => "x"
        };
        public override decimal GetDecimal(int ordinal) => 0;
        public override DateTime GetDateTime(int ordinal) => DateTime.UtcNow;
        public override object GetValue(int ordinal) => ordinal switch
        {
            0 => "SCHEMA",
            1 => "source",
            2 => 100 + _row,
            3 => "void",
            4 => false,
            5 => "desc",
            6 => $"proc_{_row}()",
            7 => "",
            8 => "sql",
            _ => DBNull.Value
        };
        public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
    }

    private sealed class FakeViewReader : DbDataReader
    {
        private int _row = -1;
        public override bool Read() => ++_row < 1;
        public override int FieldCount => 3;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override bool HasRows => true;
        public override bool NextResult() => false;
        public override int GetOrdinal(string name) => 0;
        public override string GetName(int ordinal) => ordinal.ToString();
        public override string GetDataTypeName(int ordinal) => "string";
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override int GetValues(object[] values) => 0;
        public override bool IsDBNull(int ordinal) => false;
        public override bool GetBoolean(int ordinal) => false;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => '\0';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 0;
        public override long GetInt64(int ordinal) => 0;
        public override float GetFloat(int ordinal) => 0;
        public override double GetDouble(int ordinal) => 0;
        public override string GetString(int ordinal) => ordinal switch
        {
            0 => "SCHEMA",
            1 => "V1",
            _ => "select 1"
        };
        public override decimal GetDecimal(int ordinal) => 0;
        public override DateTime GetDateTime(int ordinal) => DateTime.UtcNow;
        public override object GetValue(int ordinal) => GetString(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) => _items.ToArray().CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) { }
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) { }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }
}
