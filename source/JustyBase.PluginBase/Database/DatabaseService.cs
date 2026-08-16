using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using System.Data.Common;

namespace JustyBase.PluginDatabaseBase.Database;

public abstract partial class DatabaseService : IDatabaseService, IDatabaseWithSpecificImportService
{
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string TempDataDirectory { get; set; } = string.Empty;
    public ISimpleLogger Logger { get; set; } = ISimpleLogger.EmptyLogger;
    public CurrentAutoCompletDatabaseMode AutoCompletDatabaseMode { get; init; }
    public DatabaseTypeEnum DatabaseType { get; init; } = DatabaseTypeEnum.NotSupportedDatabase;

    private DbConnection? _connection;
    public DbConnection Connection
    {
        get
        {
            _connection ??= GetConnection(null, pooling: false);
            return _connection;
        }
        protected set
        {
            if (_connection is not null)
            {
                Connection.Disposed -= Connection_Disposed;
            }
            _connection = value;
            Connection.Disposed += Connection_Disposed;
        }
    }
    private void Connection_Disposed(object? sender, EventArgs e)
    {
        //Debug.Assert(false);
        _connection = null;//???
    }

    public int CONNECTION_TIMEOUT = 10;

    public int DEFAULT_COMMAND_TIMEOUT = 3_600;

    protected bool preferDatabaseInCodes = true;
    protected virtual string GetLimitClause(object rowsCnt)
    {
        return $"LIMIT {rowsCnt}";
    }

    public DatabaseConnectedLevel ConnectedLevel { get; set; } = DatabaseConnectedLevel.NotConnected;

    protected DatabaseService(string username, string password, string port, string ip, string db, int connectionTimeout)
    {
        Username = username;
        Password = password;
        Port = port;
        Ip = ip;
        Database = db;
        if (connectionTimeout > 0)
        {
            CONNECTION_TIMEOUT = connectionTimeout;
        }

        _cacheManager = new DatabaseCacheManager(
            _databaseSchemaTable,
            _databaseDefSchema,
            _procedureDictCache,
            _viewDictCache,
            _synonymTableDictCache);
    }

    private readonly DatabaseCacheManager _cacheManager;

    public Action<string> DbMessageAction { get; set; } = _ => { };
    //private static readonly StringPool stringPoolForSchemaGeneral = new StringPool();
    //protected static StringPool StringPoolForSchemaGeneral => stringPoolForSchemaGeneral;
    public string CleanSqlWord(string? word, CurrentAutoCompletDatabaseMode autoCompletMode)
    {
        if (word is not null && (autoCompletMode & CurrentAutoCompletDatabaseMode.MakeUpperCase) != CurrentAutoCompletDatabaseMode.NotSet)
        {
            if (!word.StartsWith('"'))
            {
                word = word.ToUpperInvariant();
            }
            else if (word.StartsWith('"') && word.EndsWith('"'))
            {
                word = word[1..^1];
            }
        }
        return word ?? string.Empty;
    }

    public bool PrefrerUpperCase = true;
    public string QuoteNameIfNeeded(string word)
    {
        if (!word.IsGoodName(PrefrerUpperCase))
        {
            word = $"\"{word.Replace("\"", "\"\"")}\"";
        }
        return word;
    }

    public virtual void ClearCachedData()
    {
        _cacheManager.ClearMainCache();
        DatabaseTableIdColumnIntervalSpan.Clear();
        DatabaseColumnsList.Clear();
    }

    public virtual void ChangeDatabaseSpecial(DbConnection con, string databaseName)
    {
        con.ChangeDatabase(databaseName);
    }
    public string ChangeDatabaseIfNeeded(DbConnection con, string selectedDatabaseName)
    {
        if (this.DatabaseType == DatabaseTypeEnum.NetezzaSQL || this.DatabaseType == DatabaseTypeEnum.PostgreSql)
        {
            if (string.IsNullOrWhiteSpace(selectedDatabaseName))
            {
                selectedDatabaseName = con.Database;
            }
            ChangeDatabaseSpecial(con, selectedDatabaseName);
            return selectedDatabaseName;
        }
        return "";
    }

    protected static readonly Lock _lock2 = new();

    public void CacheMainDictionary()
    {
        _cacheManager.CacheMainDictionary(
            DatabaseType,
            GetDatabases,
            disposeSharedConnection: () => _connection?.Dispose(),
            getConnection: GetConnection,
            configureConnection: ConfigureOpenConnection,
            loadDatabaseObject: LoadDatabaseObject,
            loadColumns: LoadColumns,
            setConnectedLevel: level => ConnectedLevel = level,
            netezza: this as INetezza,
            logger: Logger);
    }

    private void ConfigureOpenConnection(DbConnection connection)
    {
        if (this is IDatabaseConnectionConfigurator configurator)
        {
            configurator.ConfigureOpenConnection(connection);
        }
    }

    public DbCommand CreateCommandFromConnection(DbConnection con)
    {
        var cmd = con.CreateCommand();
        return cmd;
    }

    public virtual IDatabaseRowReader GetDatabaseRowReader(DbDataReader reader)
    {
        return new DatabaseRowReaderGeneral(reader);
    }
    public static string KeyNameFromChar(char c)
    {
        return c switch
        {
            'f' => "FOREIGN KEY",
            'p' => "PRIMARY KEY",
            'u' => "UNIQUE",
            _ => "TO DO"
        };
    }
    public abstract DbConnection GetConnection(string? databaseName, bool pooling = true);

}
