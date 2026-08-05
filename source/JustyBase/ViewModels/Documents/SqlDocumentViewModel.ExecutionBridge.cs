using System.Data.Common;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Documents;
using JustyBase.Models.Tools;

namespace JustyBase.ViewModels.Documents;

public sealed partial class SqlDocumentViewModel : ISqlExecutionBridge
{
    LogMessage? ISqlExecutionBridge.AddLogMessage(string message, LogMessageType type, DateTime time, string localTitle)
    {
        return AddLogMessage(message, type, time, localTitle);
    }

    void ISqlExecutionBridge.ShowProgress(int current, int total)
    {
        ShowProgress(current, total);
    }

    void ISqlExecutionBridge.HandleStandardGrid(IDatabaseService actualDatabaseService, string? resTitle, string? query, LogMessage? currentLogMessage, bool tabsWithRows, int actualQueryNum, DbDataReader rdr, DbCommand cmd, string? shortQuery)
    {
        int ubound = _executionServices.ExecutionStateService.GlobalAbortUpperBound;
        _executionServices.ResultDispatcherService.DispatchGridResult(this.Id, _resultManager, actualDatabaseService, resTitle, query, currentLogMessage, tabsWithRows, actualQueryNum, ref ubound, rdr, cmd, shortQuery);
        _executionServices.ExecutionStateService.GlobalAbortUpperBound = ubound;
    }

    void ISqlExecutionBridge.HandleAnotherResult(LogMessage? currentLogMessage, DbDataReader rdr)
    {
        _executionServices.ResultDispatcherService.DispatchRecordsAffected(currentLogMessage, rdr);
    }

    void ISqlExecutionBridge.ErrorMessageToUi(string localTitle, LogMessage? currentLogMessage, int actualQueryNum, IDatabaseService actualDatabaseService, int currentSqlNumber, string sql, DbCommand cmd, Exception exx1)
    {
        int ubound = _executionServices.ExecutionStateService.GlobalAbortUpperBound;
        _executionServices.ResultDispatcherService.DispatchErrorResult(this.Id, _resultManager, localTitle, currentLogMessage, actualQueryNum, ref ubound, actualDatabaseService, currentSqlNumber, sql, cmd, exx1);
        _executionServices.ExecutionStateService.GlobalAbortUpperBound = ubound;
    }

    void ISqlExecutionBridge.ClosePreviousResultsIfNeeded()
    {
        _executionServices.ResultDispatcherService.ClosePreviousResults(this.Id, _resultManager);
    }



    DbConnection ISqlExecutionBridge.GetConToGo(bool doPooling, bool keepConnectionOpenLocal, IDatabaseService service)
    {
        return _executionServices.ConnectionManager.GetOrCreateConnection(doPooling, keepConnectionOpenLocal, service);
    }

    DbConnection? ISqlExecutionBridge.TryReconnectOnce(IDatabaseService service, DbConnection broken, bool doPooling, bool keepOpen, bool isCancelled, Exception? cause)
    {
        return _executionServices.ConnectionManager.TryReconnectOnce(service, broken, doPooling, keepOpen, isCancelled, cause);
    }

    void ISqlExecutionBridge.ResetReconnectCounter()
    {
        _executionServices.ConnectionManager.ResetReconnectCounter();
    }

    void ISqlExecutionBridge.AddToHistory(
        string connectionName,
        string database,
        string queryText,
        HistoryRunStatus status,
        long durationMs,
        string? errorMessage)
    {
        AddToHistory(connectionName, database, queryText, status, durationMs, errorMessage);
    }

    void ISqlExecutionBridge.TrackQueryState(int globalQueryNum, DbCommand cmd, SqlCommandState state)
    {
        _executionServices.ExecutionStateService.TrackCommandState(globalQueryNum, cmd, state);
    }

    void ISqlExecutionBridge.SelectError(int position, int length)
    {
        _messageForUserTools.DispatcherActionInstance(() => SqlEditor.SelectError(position, length));
    }

    void ISqlExecutionBridge.RefreshDatabaseList(IDatabaseService actualDatabaseService)
    {
        RefreshDatabaseList(actualDatabaseService);
    }



    void ISqlExecutionBridge.FlashWindowExIfNeeded()
    {
        _messageForUserTools.FlashWindowExIfNeeded();
    }
}
