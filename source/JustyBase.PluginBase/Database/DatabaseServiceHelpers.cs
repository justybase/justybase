using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.PluginDatabaseBase.Database;

/// <summary>
/// Static utilities (driver name maps, enum helpers) plus thin delegates to
/// <see cref="DatabaseServiceRegistry.Shared"/> for connection cache/factory ownership.
/// Prefer injecting <c>IDatabaseServiceResolver</c> from the app host when possible.
/// </summary>
public static class DatabaseServiceHelpers
{
    private static readonly string[] _typeInDatabaseToNameInSchema;
    private static readonly Dictionary<string, TypeInDatabaseEnum> _schemaNameToTypeInDatabaseEnum;

    static DatabaseServiceHelpers()
    {
        var enumValues = Enum.GetValues<TypeInDatabaseEnum>();
        int enumElements = enumValues.Length;
        _typeInDatabaseToNameInSchema = new string[enumElements];
        foreach (TypeInDatabaseEnum item in Enum.GetValues<TypeInDatabaseEnum>())
        {
            _typeInDatabaseToNameInSchema[(int)item] = item.ToString();
        }
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Table] = "Table";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.View] = "View";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Procedure] = "Procedure";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.ExternalTable] = "External table";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Synonym] = "Synonym";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Function] = "Function";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Fluid] = "Fluid";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Index] = "Index";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Partition] = "Partition";
        _typeInDatabaseToNameInSchema[(int)TypeInDatabaseEnum.Trigger] = "Trigger";
        _schemaNameToTypeInDatabaseEnum = new Dictionary<string, TypeInDatabaseEnum>(enumElements);
        for (int i = 0; i < enumElements; i++)
        {
            _schemaNameToTypeInDatabaseEnum[_typeInDatabaseToNameInSchema[i]] = (TypeInDatabaseEnum)i;
        }
    }

    public static string ToStringEx(this TypeInDatabaseEnum typeInDatabase)
        => _typeInDatabaseToNameInSchema[(int)typeInDatabase];

    public static TypeInDatabaseEnum FromStringEx(string name)
    {
        if (_schemaNameToTypeInDatabaseEnum.TryGetValue(name, out var res))
        {
            return res;
        }

        return TypeInDatabaseEnum.otherNoneEntry;
    }

    public static TypeInDatabaseEnum GetTypeInDatabaseEnumFromDbName(this string typeName)
    {
        return typeName switch
        {
            "TABLE" => TypeInDatabaseEnum.Table,
            "BASE TABLE" => TypeInDatabaseEnum.Table,
            "TYPED TABLE" => TypeInDatabaseEnum.Table,
            "HIERARCHY TABLE" => TypeInDatabaseEnum.Table,
            "DETACHED TABLE" => TypeInDatabaseEnum.Table,
            "MATERIALIZED QUERY TABLE" => TypeInDatabaseEnum.Table,
            "ALIAS" => TypeInDatabaseEnum.db2alias,
            "VIEW" => TypeInDatabaseEnum.View,
            "TYPED VIEW" => TypeInDatabaseEnum.View,
            "PROCEDURE" => TypeInDatabaseEnum.Procedure,
            "FUNCTION" => TypeInDatabaseEnum.Function,
            "SEQUENCE" => TypeInDatabaseEnum.Sequence,
            "IDENTITY SEQUENCE" => TypeInDatabaseEnum.Sequence,
            "SYNONYM" => TypeInDatabaseEnum.Synonym,
            "NICKNAME" => TypeInDatabaseEnum.Synonym,
            "EXTERNAL TABLE" => TypeInDatabaseEnum.ExternalTable,
            "AGGREGATE" => TypeInDatabaseEnum.thisAggregate,
            "FLUID" => TypeInDatabaseEnum.Fluid,
            "INDEX" => TypeInDatabaseEnum.Index,
            "TRIGGER" => TypeInDatabaseEnum.Trigger,
            "PARTITION" => TypeInDatabaseEnum.Partition,
            "PARTITION TABLE" => TypeInDatabaseEnum.Partition,
            _ => TypeInDatabaseEnum.otherNoneGroup
        };
    }

    private static readonly Dictionary<string, DatabaseTypeEnum> _textToDatabaseTypeEnumDict = new()
    {
        {"NetezzaSQL", DatabaseTypeEnum.NetezzaSQL},
        {"DB2", DatabaseTypeEnum.DB2},
        {"MsSqlTrusted", DatabaseTypeEnum.MsSqlTrusted},
        {"Postgres", DatabaseTypeEnum.PostgreSql},
        {"Oracle", DatabaseTypeEnum.Oracle},
        {"SQLite", DatabaseTypeEnum.Sqlite},
        {"DuckDB", DatabaseTypeEnum.DuckDB},
        {"MySQL", DatabaseTypeEnum.MySql},
    };

    public static List<string> GetSupportedDriversNames()
        => _textToDatabaseTypeEnumDict.Keys.ToList();

    public static DatabaseTypeEnum StringToDatabaseTypeEnum(string? driver)
    {
        if (driver is not null && _textToDatabaseTypeEnumDict.TryGetValue(driver, out var tpe))
        {
            return tpe;
        }

        return DatabaseTypeEnum.NotSupportedDatabase;
    }

    private static DatabaseServiceRegistry Registry => DatabaseServiceRegistry.Shared;

    public static event Action? SchemaCacheLoaded
    {
        add => Registry.SchemaCacheLoaded += value;
        remove => Registry.SchemaCacheLoaded -= value;
    }

    public static void RemoveCachedConnection(string connectionName)
        => Registry.RemoveCachedConnection(connectionName);

    public static void AddDatabaseImplementation(
        DatabaseTypeEnum databaseTypeEnum,
        Func<string, string, string, string, string, int, IDatabaseService> ctorOfDbService)
        => Registry.AddDatabaseImplementation(databaseTypeEnum, ctorOfDbService);

    public static IDatabaseService? GetDatabaseService(
        IDatabaseInfo? databaseInfo,
        string connectionName,
        bool forceRefresh = false,
        bool delayCache = false,
        int connectionTimeout = 0,
        Action<string>? messageAction = null,
        IDatabaseService? ownDatabaseService = null)
        => Registry.GetDatabaseService(
            databaseInfo,
            connectionName,
            forceRefresh,
            delayCache,
            connectionTimeout,
            messageAction,
            ownDatabaseService);

    public static bool IsDriverRegistered(IDatabaseInfo? databaseInfo, string connectionName)
        => Registry.IsDriverRegistered(databaseInfo, connectionName);

    public static bool IsDatabaseConnected(string connectionName)
        => Registry.IsDatabaseConnected(connectionName);

    public static DatabaseConnectedLevel GetDatabaseConnectedLevel(string connectionName)
        => Registry.GetDatabaseConnectedLevel(connectionName);

    public static IReadOnlyList<IDatabaseService> GetCachedServices()
        => Registry.GetCachedServices();
}
