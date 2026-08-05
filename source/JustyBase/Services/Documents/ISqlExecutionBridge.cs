using System.Data.Common;
using JustyBase.Models.Tools;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public interface ISqlExecutionBridge
{
    LogMessage? AddLogMessage(string message, LogMessageType type, DateTime time, string localTitle);
    void ShowProgress(int current, int total);
    void HandleStandardGrid(IDatabaseService actualDatabaseService, string? resTitle, string? query, LogMessage? currentLogMessage, bool tabsWithRows, int actualQueryNum, DbDataReader rdr, DbCommand cmd, string? shortQuery);
    void HandleAnotherResult(LogMessage? currentLogMessage, DbDataReader rdr);
    void ErrorMessageToUi(string localTitle, LogMessage? currentLogMessage, int actualqlobalQueryNum, IDatabaseService actualDatabaseService, int currentLocalSqlNumber, string sql, DbCommand cmd, Exception exx1);
    void FlashWindowExIfNeeded();
    void ClosePreviousResultsIfNeeded();
    DbConnection GetConToGo(bool doPooling, bool keepConnectionOpenLocal, IDatabaseService service);
    DbConnection? TryReconnectOnce(IDatabaseService service, DbConnection broken, bool doPooling, bool keepOpen, bool isCancelled, Exception? cause);
    void ResetReconnectCounter();
    void AddToHistory(
        string connectionName,
        string database,
        string queryText,
        HistoryRunStatus status,
        long durationMs,
        string? errorMessage);
    void TrackQueryState(int globalQueryNum, DbCommand cmd, SqlCommandState state);
    void RefreshDatabaseList(IDatabaseService actualDatabaseService);

    void SelectError(int position, int length);
}
