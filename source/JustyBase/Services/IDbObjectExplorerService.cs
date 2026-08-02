using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;

namespace JustyBase.Services;

/// <summary>
/// Service responsible for generating SQL code for database object operations
/// (DDL, DROP, RENAME, GROOM, SELECT, CREATE FROM) and finding database objects by name.
/// </summary>
public interface IDbObjectExplorerService
{
    Task<IDatabaseService?> EnsureDatabaseServiceAsync(IDatabaseService? current, string connectionName);
    string GetDropCode(IDatabaseService databaseService, string tappedWord);
    Task<string> GetDdlCode(IDatabaseService databaseService, string database, string schema, string objectName);
    Task<string> GetRecreateCode(IDatabaseService databaseService, string database, string schema, string objectName);
    string GetRenameCode(IDatabaseService databaseService, string tappedWord);
    string GetCreateFromCode(IDatabaseService databaseService, string tappedWord);
    string GetGroomCode(IDatabaseService databaseService, string tappedWord);
    string GetGenerateStatsCode(IDatabaseService databaseService, string tappedWord);
    string GetSelectCode(IDatabaseService databaseService, string tappedWord);
    (DatabaseObject? dbObject, string? schema, string? database) FindFromName(
        IDatabaseService databaseService, string tappedWord, bool cleanNames, string? selectedDatabase);
}
