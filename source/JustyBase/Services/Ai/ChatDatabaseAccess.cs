using JustyBase.Ai.Ports;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services.Documents;

namespace JustyBase.Services.Ai;

/// <summary>
/// Resolves the shared chat database port over the host's database service registry.
/// </summary>
public sealed class ChatDatabaseAccessProvider : IChatDatabaseAccessProvider
{
    private readonly IDatabaseServiceResolver _resolver;
    private readonly IGeneralApplicationData _generalApplicationData;

    public ChatDatabaseAccessProvider(
        IDatabaseServiceResolver resolver,
        IGeneralApplicationData generalApplicationData)
    {
        _resolver = resolver;
        _generalApplicationData = generalApplicationData;
    }

    public IChatDatabaseAccess? GetDatabaseAccess(string connectionName)
    {
        var service = _resolver.GetDatabaseService(_generalApplicationData, connectionName);
        return service is null ? null : new ChatDatabaseAccess(service);
    }
}

/// <summary>Adapter over the host <see cref="IDatabaseService"/> for the shared chat tools.</summary>
public sealed class ChatDatabaseAccess : IChatDatabaseAccess
{
    private readonly IDatabaseService _service;

    public ChatDatabaseAccess(IDatabaseService service)
    {
        _service = service;
    }

    public string Database => _service.Database;

    public IReadOnlyList<string> GetSchemas(string databaseName, string schemaPattern)
        => _service.GetSchemas(databaseName, schemaPattern).ToList();

    public IReadOnlyList<ChatDatabaseObject> GetDbObjects(
        string databaseName,
        string schemaName,
        string objectPattern,
        ChatObjectType type)
    {
        return _service.GetDbObjects(databaseName, schemaName, objectPattern, MapType(type))
            .Select(o => new ChatDatabaseObject(o.Name, o.Desc))
            .ToList();
    }

    public IReadOnlyList<ChatDatabaseColumn> GetColumns(
        string databaseName,
        string schemaName,
        string objectName,
        string columnPattern)
    {
        return _service.GetColumns(databaseName, schemaName, objectName, columnPattern)
            .Select(c => new ChatDatabaseColumn(c.Name, c.FullTypeName))
            .ToList();
    }

    public async Task<string?> GetCreateTableTextAsync(string database, string schema, string table)
        => await _service.GetCreateTableText(database, schema, table).ConfigureAwait(false);

    public async Task<string?> GetCreateViewTextAsync(string database, string schema, string view)
        => await _service.GetCreateViewText(database, schema, view).ConfigureAwait(false);

    public async Task<string?> GetCreateProcedureTextAsync(string database, string schema, string procedure)
        => await _service.GetCreateProcedureText(database, schema, procedure, forceFreshCode: true).ConfigureAwait(false);

    public async Task<string?> GetCreateExternalTextAsync(string database, string schema, string externalTable)
        => await _service.GetCreateExternalText(database, schema, externalTable).ConfigureAwait(false);

    public async Task<string?> GetCreateSynonymTextAsync(string database, string schema, string synonym)
        => await _service.GetCreateSynonymText(database, schema, synonym).ConfigureAwait(false);

    public async Task<string?> GetCreateIndexTextAsync(string database, string schema, string index)
        => await _service.GetCreateIndexText(database, schema, index).ConfigureAwait(false);

    public async Task<string?> GetCreatePartitionTextAsync(string database, string schema, string partition)
        => await _service.GetCreatePartitionText(database, schema, partition).ConfigureAwait(false);

    public string GetCheckDistributeText(string database, string schema, string table)
        => _service.GetCheckDistributeText(database, schema, table);

    public async Task<int> ExecuteNonQueryAsync(
        string sql,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _service.GetConnection(databaseName, pooling: false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (_service is IDatabaseConnectionConfigurator configurator)
        {
            configurator.ConfigureOpenConnection(connection);
        }
        await using var command = _service.CreateCommandFromConnection(connection);
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<string>? TryGetDistributionColumns(string database, string schema, string table)
    {
        if (_service is not INetezza netezza)
        {
            return null;
        }

        if (!netezza.DistributionDictionary.ContainsKey(database))
            netezza.FillDistInfoForDatabase(database);

        return netezza.DistributionDictionary.TryGetValue(database, out var bySchema)
            && bySchema.TryGetValue(schema, out var byTable)
            && byTable.TryGetValue(table, out var columns)
            && columns.Count > 0
                ? columns
                : null;
    }

    public IReadOnlyList<string>? TryGetOrganizeColumns(string database, string schema, string table)
    {
        if (_service is not INetezza netezza)
        {
            return null;
        }

        return netezza.OrganizeDictionary.TryGetValue(database, out var bySchema)
            && bySchema.TryGetValue(schema, out var byTable)
            && byTable.TryGetValue(table, out var columns)
            && columns.Count > 0
                ? columns
                : null;
    }

    private static TypeInDatabaseEnum MapType(ChatObjectType type) => type switch
    {
        ChatObjectType.Table => TypeInDatabaseEnum.Table,
        ChatObjectType.View => TypeInDatabaseEnum.View,
        ChatObjectType.Procedure => TypeInDatabaseEnum.Procedure,
        ChatObjectType.Function => TypeInDatabaseEnum.Function,
        ChatObjectType.ExternalTable => TypeInDatabaseEnum.ExternalTable,
        ChatObjectType.Synonym => TypeInDatabaseEnum.Synonym,
        ChatObjectType.Fluid => TypeInDatabaseEnum.Fluid,
        ChatObjectType.Index => TypeInDatabaseEnum.Index,
        ChatObjectType.Partition => TypeInDatabaseEnum.Partition,
        _ => TypeInDatabaseEnum.otherNoneEntry
    };
}
