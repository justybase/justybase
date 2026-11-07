namespace JustyBase.PluginCommon.Models;

/// <summary>
/// Typed connection coordinates for database service factories.
/// Replaces the positional <c>(username, password, port, ip, db, connectionTimeout)</c>
/// argument list that was easy to misorder at call sites.
/// Property names mirror <see cref="Contracts.IDatabaseConnectionInfo"/> so mapping is mechanical.
/// </summary>
public sealed record DbConnectionOptions(
    string Username = "",
    string Password = "",
    string Port = "",
    string Ip = "",
    string Database = "",
    int ConnectionTimeout = 0)
{
    /// <summary>Db2-only connection options; null when not configured.</summary>
    public Db2ConnectionOptions? Db2Options { get; init; }
}
