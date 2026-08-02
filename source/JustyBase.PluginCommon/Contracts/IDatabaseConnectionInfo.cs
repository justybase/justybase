using JustyBase.PluginCommon.Enums;
using System.Data.Common;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseConnectionInfo
{
    string Database { get; set; }
    string Ip { get; set; }
    string Name { get; set; }
    string Password { get; set; }
    string Port { get; set; }
    string Username { get; set; }
    string TempDataDirectory { get; set; }
    ISimpleLogger Logger { get; set; }
    DbConnection Connection { get; }
    Action<string> DbMessageAction { get; set; }
    CurrentAutoCompletDatabaseMode AutoCompletDatabaseMode { get; init; }
    DatabaseConnectedLevel ConnectedLevel { get; set; }

    void ChangeDatabaseSpecial(DbConnection con, string databaseName);
    string ChangeDatabaseIfNeeded(DbConnection con, string selectedDatabaseName);
    DbCommand CreateCommandFromConnection(DbConnection con);
    DbConnection GetConnection(string? databaseName, bool pooling = true);
    IDatabaseRowReader GetDatabaseRowReader(DbDataReader reader);
}
