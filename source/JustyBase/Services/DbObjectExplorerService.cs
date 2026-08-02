using JustyBase.Common.Contracts;
using JustyBase.Helpers.Shared;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services.Documents;

namespace JustyBase.Services;

public class DbObjectExplorerService : IDbObjectExplorerService
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    public DbObjectExplorerService(
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _databaseServiceResolver = databaseServiceResolver;
    }

    public async Task<IDatabaseService?> EnsureDatabaseServiceAsync(IDatabaseService? current, string connectionName)
    {
        if (current is null || current.Name != connectionName)
        {
            return await Task.Run(() => _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName));
        }
        return current;
    }

    public string GetDropCode(IDatabaseService databaseService, string tappedWord)
        => databaseService.GetTableDropCode(tappedWord);

    public async Task<string> GetDdlCode(IDatabaseService databaseService, string database, string schema, string objectName)
        => await databaseService.GetCreateTableText(database, schema, objectName);

    public async Task<string> GetRecreateCode(IDatabaseService databaseService, string database, string schema, string objectName)
        => await databaseService.GetReCreateTableText(database, schema, objectName);

    public string GetRenameCode(IDatabaseService databaseService, string tappedWord)
        => databaseService.GetTableRenameCode(tappedWord);

    public string GetCreateFromCode(IDatabaseService databaseService, string tappedWord)
        => databaseService.GetCreateFromCode(tappedWord);

    public string GetGroomCode(IDatabaseService databaseService, string tappedWord)
        => databaseService.GetGroom(null, null, tappedWord);

    public string GetGenerateStatsCode(IDatabaseService databaseService, string tappedWord)
        => databaseService.GetGenerateStats(null, null, tappedWord);

    public string GetSelectCode(IDatabaseService databaseService, string tappedWord)
        => databaseService.GetShortSelectCode(tappedWord);

    public (DatabaseObject? dbObject, string? schema, string? database) FindFromName(
        IDatabaseService databaseService, string tappedWord, bool cleanNames, string? selectedDatabase)
    {
        var m = SqlDocumentViewModelHelper.DatabaseSchemaTableRegex.Match(tappedWord);

        if (m.Success)
        {
            var database = m.Groups["part1"].Value;
            var schema = m.Groups["part2"].Value;
            var name = m.Groups["part3"].Value;
            if (string.IsNullOrEmpty(database))
            {
                database = selectedDatabase ?? databaseService.Database;
            }

            var o = databaseService.FindDbObject(database, schema, name, cleanNames);
            if (o.Count() == 1)
            {
                var result = o.FirstOrDefault();
                return (result.dbObject, result.schema, database);
            }
        }
        return (null, null, null);
    }
}
