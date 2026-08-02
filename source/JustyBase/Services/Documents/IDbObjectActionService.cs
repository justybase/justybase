using System;
using System.Threading.Tasks;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public class DbObjectActionResult
{
    public string? TextToInsert { get; init; }
    public string[]? PathToExpand { get; init; }
    public IDatabaseService? UpdatedDatabaseService { get; init; }
    public bool ShowWarningNoConnection { get; init; }
}

public interface IDbObjectActionService
{
    Task<DbObjectActionResult> ExecuteObjectActionAsync(
        string optionName, 
        string tappedWord, 
        string selectedConnectionName, 
        string selectedDatabase, 
        IDatabaseService? currentDatabaseService);
}
