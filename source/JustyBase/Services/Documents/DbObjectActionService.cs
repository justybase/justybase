using System;
using System.Threading.Tasks;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Helpers.Shared;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Services.Documents;

public sealed class DbObjectActionService : IDbObjectActionService
{
    private readonly IDbObjectExplorerService _dbObjectExplorerService;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IMessageForUserTools _messageForUserTools;

    public DbObjectActionService(
        IDbObjectExplorerService dbObjectExplorerService,
        ISimpleLogger simpleLogger,
        IMessageForUserTools messageForUserTools)
    {
        _dbObjectExplorerService = dbObjectExplorerService;
        _simpleLogger = simpleLogger;
        _messageForUserTools = messageForUserTools;
    }

    public async Task<DbObjectActionResult> ExecuteObjectActionAsync(
        string optionName, 
        string tappedWord, 
        string selectedConnectionName, 
        string selectedDatabase, 
        IDatabaseService? currentDatabaseService)
    {
        var result = new DbObjectActionResult();
        try
        {
            var dbService = await _dbObjectExplorerService.EnsureDatabaseServiceAsync(currentDatabaseService, selectedConnectionName);
            
            if (dbService is null)
            {
                result = new DbObjectActionResult { ShowWarningNoConnection = true };
                return result;
            }

            result = new DbObjectActionResult { UpdatedDatabaseService = dbService };
            string? txt = "no option found";

            switch (optionName)
            {
                case SqlDocumentViewModelHelper.CurrentOptionsListDROP:
                    txt = _dbObjectExplorerService.GetDropCode(dbService, tappedWord);
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListDDL:
                case SqlDocumentViewModelHelper.CurrentOptionsListRECREATE:
                    var found = _dbObjectExplorerService.FindFromName(dbService, tappedWord, true, selectedDatabase);
                    if (found.dbObject is not null)
                    {
                        if (optionName == "Ddl" || optionName == SqlDocumentViewModelHelper.CurrentOptionsListDDL)
                        {
                            txt = await _dbObjectExplorerService.GetDdlCode(dbService, found.database!, found.schema!, found.dbObject.Name);
                        }
                        else if (optionName == "Recreate" || optionName == SqlDocumentViewModelHelper.CurrentOptionsListRECREATE)
                        {
                            txt = await _dbObjectExplorerService.GetRecreateCode(dbService, found.database!, found.schema!, found.dbObject.Name);
                        }
                    }
                    else
                    {
                        txt = "to many or no results";
                    }
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListRENAME:
                    txt = _dbObjectExplorerService.GetRenameCode(dbService, tappedWord);
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListCREATE_FROM:
                    txt = _dbObjectExplorerService.GetCreateFromCode(dbService, tappedWord);
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListGROOM:
                    txt = _dbObjectExplorerService.GetGroomCode(dbService, tappedWord);
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListSTATS:
                    txt = _dbObjectExplorerService.GetGenerateStatsCode(dbService, tappedWord);
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListSELECT:
                    txt = _dbObjectExplorerService.GetSelectCode(dbService, tappedWord);
                    break;
                case SqlDocumentViewModelHelper.CurrentOptionsListJUMP_TO:
                    txt = null;
                    var found1 = _dbObjectExplorerService.FindFromName(dbService, tappedWord, true, selectedDatabase);
                    if (found1.dbObject is not null)
                    {
                        string nme = found1.dbObject.Name;
                        nme = dbService.CleanSqlWord(nme, dbService.AutoCompletDatabaseMode);
                        var schema1 = dbService.CleanSqlWord(found1.schema!, dbService.AutoCompletDatabaseMode);
                        var database1 = dbService.CleanSqlWord(found1.database!, dbService.AutoCompletDatabaseMode);

                        string[] toExpandPath = new SchemaSearchItem()
                        {
                            Name = nme,
                            Db = database1,
                            Schema = schema1,
                            Type = found1.dbObject.TypeInDatabase.ToStringEx()
                        }.GetPath(selectedConnectionName);

                        if (toExpandPath.Length > 0)
                        {
                            result = new DbObjectActionResult { UpdatedDatabaseService = dbService, PathToExpand = toExpandPath };
                        }
                    }
                    break;
            }

            result = new DbObjectActionResult 
            { 
                UpdatedDatabaseService = result.UpdatedDatabaseService ?? dbService, 
                PathToExpand = result.PathToExpand,
                TextToInsert = txt
            };
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: true);
            _messageForUserTools.ShowSimpleMessageBoxInstance($"ERROR {ex.Message}");
        }

        return result;
    }
}
