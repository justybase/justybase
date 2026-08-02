using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Services.Documents;

public interface IDatabaseServiceResolver
{
    bool IsDriverRegistered(IGeneralApplicationData generalApplicationData, string connectionName);

    IDatabaseService? GetDatabaseService(
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        bool delayCache = false,
        bool forceRefresh = false,
        Action<string>? messageAction = null);

    void RemoveCachedConnection(string connectionName);

    IReadOnlyList<IDatabaseService> GetCachedServices();

    event Action? SchemaCacheLoaded;
}

/// <summary>
/// DI-owned façade over <see cref="DatabaseServiceRegistry"/> (singleton ownership of cache/factories).
/// </summary>
public sealed class DatabaseServiceResolver : IDatabaseServiceResolver
{
    private readonly DatabaseServiceRegistry _registry;

    public DatabaseServiceResolver(DatabaseServiceRegistry registry)
    {
        _registry = registry;
    }

    public event Action? SchemaCacheLoaded
    {
        add => _registry.SchemaCacheLoaded += value;
        remove => _registry.SchemaCacheLoaded -= value;
    }

    public bool IsDriverRegistered(IGeneralApplicationData generalApplicationData, string connectionName)
        => _registry.IsDriverRegistered(generalApplicationData, connectionName);

    public IDatabaseService? GetDatabaseService(
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        bool delayCache = false,
        bool forceRefresh = false,
        Action<string>? messageAction = null)
        => _registry.GetDatabaseService(
            generalApplicationData,
            connectionName,
            forceRefresh,
            delayCache,
            messageAction: messageAction);

    public void RemoveCachedConnection(string connectionName)
        => _registry.RemoveCachedConnection(connectionName);

    public IReadOnlyList<IDatabaseService> GetCachedServices()
        => _registry.GetCachedServices();
}
