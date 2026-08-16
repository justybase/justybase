using System.Data.Common;

namespace JustyBase.PluginCommon.Contracts;

/// <summary>
/// Allows a driver to apply connection-local session state after the host opens
/// a DbConnection (for example SQLite ATTACH databases and PRAGMAs).
/// </summary>
public interface IDatabaseConnectionConfigurator
{
    void ConfigureOpenConnection(DbConnection connection);
}
