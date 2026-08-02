using System.Data.Common;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services.Docking;

public interface IDockResultRoutingService
{
    void AddResult(
        (IDatabaseService? dbService, DbDataReader? rdr, string errorMessage) result,
        string documentId,
        SqlDocumentViewModel document,
        int queryNum,
        ref int abortUpperBound,
        string? sql,
        DbCommand? command,
        string? title,
        SqlResultsFastViewModel resultsHost,
        bool isDocumentActive);

    void SyncActiveDocumentResults(
        SqlDocumentViewModel document,
        SqlResultsFastViewModel resultsHost,
        Action<string> switchLogsAction);
}

public sealed class DockResultRoutingService(IDockViewModelFactory viewModelFactory, IMessageForUserTools messageForUserTools) : IDockResultRoutingService
{
    private readonly IDockViewModelFactory _viewModelFactory = viewModelFactory;
    private readonly IMessageForUserTools _messageForUserTools = messageForUserTools;

    public void AddResult(
        (IDatabaseService? dbService, DbDataReader? rdr, string errorMessage) result,
        string documentId,
        SqlDocumentViewModel document,
        int queryNum,
        ref int abortUpperBound,
        string? sql,
        DbCommand? command,
        string? title,
        SqlResultsFastViewModel resultsHost,
        bool isDocumentActive)
    {
        SqlResultsViewModel resultViewModel = _viewModelFactory.CreateSqlResultsViewModel();
        resultViewModel.Id = $"ID_RESULT_{Guid.NewGuid()}_{documentId}";
        resultViewModel.RelatedSqlDocumentId = documentId;
        resultViewModel.Title = title ?? document.TitleFromDocumentVm;
        resultViewModel.CanPin = false;
        resultViewModel.CanFloat = false;
        resultViewModel.CanClose = true;
        DockCapabilityHelper.SyncOverridesFromFlags(resultViewModel);
        resultViewModel.SQL = sql;

        resultViewModel.LoadData(result);
        resultsHost.Add(resultViewModel, document, isDocumentActive);

        resultViewModel.GridEnabled = false;
        if (result.rdr is not null && command is not null && result.rdr.HasRows && result.rdr.FieldCount > 0)
        {
            resultViewModel.LoadRest(result.dbService, result.rdr, queryNum, ref abortUpperBound, command);
            return;
        }

        // No LoadRest (e.g. 0-row schema-only result): clear loading left on by LoadData.
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            resultViewModel.GridEnabled = true;
            resultViewModel.DataLoadingInProgress = false;
        });
    }

    public void SyncActiveDocumentResults(
        SqlDocumentViewModel document,
        SqlResultsFastViewModel resultsHost,
        Action<string> switchLogsAction)
    {
        resultsHost.ShowDocumentResult(document);
        switchLogsAction(document.Id);
    }
}
