using System.Reflection;
using System.Data;
using System.Data.Common;
using JustyBase.PluginCommon.Models;
using Snowflake.Data.Client;

namespace JustyBase.Tests;

public class SnowflakePluginTests
{
    [Fact]
    public void GetSqlOfColumns_DoesNotDependOnDataTypeAlias()
    {
        Type? snowflakeType = Type.GetType("SnowflakePlugin.Snowflake, SnowflakePlugin", throwOnError: false);

        Assert.NotNull(snowflakeType);

        var service = Activator.CreateInstance(snowflakeType!, ["user", "password", "", "account", "TEST_DB", 10]);
        var method = snowflakeType.GetMethod("GetSqlOfColumns", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.NotNull(service);

        var sql = Assert.IsType<string>(method!.Invoke(service!, ["TEST_DB"]));

        Assert.DoesNotContain("DATA_TYPE_ALIAS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".INFORMATION_SCHEMA.COLUMNS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeDatabaseSpecial_OpensClosedConnection()
    {
        Type? snowflakeType = Type.GetType("SnowflakePlugin.Snowflake, SnowflakePlugin", throwOnError: false);

        Assert.NotNull(snowflakeType);

        var service = Activator.CreateInstance(snowflakeType!, ["user", "password", "", "account", "TEST_DB", 10]);
        var method = snowflakeType.GetMethod("ChangeDatabaseSpecial", BindingFlags.Instance | BindingFlags.Public);
        using var fakeConnection = new FakeDbConnection();

        Assert.NotNull(method);
        Assert.NotNull(service);

        method!.Invoke(service!, [fakeConnection, "TEST_DB"]);

        Assert.Equal(1, fakeConnection.OpenCount);
        Assert.Single(fakeConnection.ExecutedCommandTexts);
        Assert.Contains("USE DATABASE", fakeConnection.ExecutedCommandTexts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangeDatabaseSpecial_WhenConfiguredSchemaDoesNotExist_IgnoresSchemaSwitchError()
    {
        Type? snowflakeType = Type.GetType("SnowflakePlugin.Snowflake, SnowflakePlugin", throwOnError: false);

        Assert.NotNull(snowflakeType);

        var service = Activator.CreateInstance(snowflakeType!, ["user", "password", "", "account", "TEST_DB", 10]);
        var applyLoginDataMethod = snowflakeType.GetMethod("ApplyLoginData", BindingFlags.Instance | BindingFlags.Public);
        var changeDatabaseMethod = snowflakeType.GetMethod("ChangeDatabaseSpecial", BindingFlags.Instance | BindingFlags.Public);
        using var fakeConnection = new FakeDbConnection
        {
            ExecuteNonQueryExceptionFactory = commandText =>
                commandText.Contains("USE SCHEMA", StringComparison.OrdinalIgnoreCase)
                    ? new SnowflakeDbException("02000", 2043, "Object does not exist, or operation cannot be performed.", "query-id")
                    : null
        };

        Assert.NotNull(service);
        Assert.NotNull(applyLoginDataMethod);
        Assert.NotNull(changeDatabaseMethod);

        applyLoginDataMethod!.Invoke(service!, [new LoginDataModel
        {
            ConnectionName = "snowflake",
            Driver = "Snowflake",
            Schema = "MISSING_SCHEMA"
        }]);

        var exception = Record.Exception(() => changeDatabaseMethod!.Invoke(service!, [fakeConnection, "TEST_DB"]));

        Assert.Null(exception);
        Assert.Equal(1, fakeConnection.OpenCount);
        Assert.Equal(2, fakeConnection.ExecutedCommandTexts.Count);
        Assert.Contains("USE DATABASE", fakeConnection.ExecutedCommandTexts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USE SCHEMA", fakeConnection.ExecutedCommandTexts[1], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public int OpenCount { get; private set; }
        public List<string> ExecutedCommandTexts { get; } = [];
        public Func<string, Exception?>? ExecuteNonQueryExceptionFactory { get; init; }

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "TEST_DB";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            OpenCount++;
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            return new FakeDbCommand(this);
        }
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private readonly DbParameterCollection _parameters = new FakeDbParameterCollection();

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            if (_connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException("Connection must be open.");
            }

            _connection.ExecutedCommandTexts.Add(CommandText);
            Exception? configuredException = _connection.ExecuteNonQueryExceptionFactory?.Invoke(CommandText);
            if (configuredException is not null)
            {
                throw configuredException;
            }
            return 1;
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            throw new NotSupportedException();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        public override int Count => 0;
        public override object SyncRoot => this;

        public override int Add(object value) => throw new NotSupportedException();
        public override void AddRange(Array values) => throw new NotSupportedException();
        public override void Clear()
        {
        }

        public override bool Contains(object value) => false;
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) => throw new NotSupportedException();
        public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
        public override int IndexOf(object value) => -1;
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => throw new NotSupportedException();
        public override void Remove(object value) => throw new NotSupportedException();
        public override void RemoveAt(int index) => throw new NotSupportedException();
        public override void RemoveAt(string parameterName) => throw new NotSupportedException();

        protected override DbParameter GetParameter(int index) => throw new NotSupportedException();
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        protected override void SetParameter(int index, DbParameter value) => throw new NotSupportedException();
        protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
    }
}
