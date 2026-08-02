using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Common.Contracts;
using JustyBase.Models.Tools;
using JustyBase.Helpers.Shared;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Common.Tools;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using JustyBase.Services;

namespace JustyBase.Services.Documents;

public class SqlExecutionService : ISqlExecutionService
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISqlVariableProcessor _sqlVariableProcessor;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private readonly SqlExecutionErrorStore _sqlExecutionErrorStore;

    public SqlExecutionService(
        IGeneralApplicationData generalApplicationData,
        ISimpleLogger simpleLogger,
        IMessageForUserTools messageForUserTools,
        ISqlVariableProcessor sqlVariableProcessor,
        IDatabaseServiceResolver databaseServiceResolver,
        SqlExecutionErrorStore sqlExecutionErrorStore)
    {
        _generalApplicationData = generalApplicationData;
        _simpleLogger = simpleLogger;
        _messageForUserTools = messageForUserTools;
        _sqlVariableProcessor = sqlVariableProcessor;
        _databaseServiceResolver = databaseServiceResolver;
        _sqlExecutionErrorStore = sqlExecutionErrorStore;
    }

    public async Task ExecuteSqlAsync(
        ISqlExecutionBridge bridge,
        SqlDocumentViewModelHelper.SqlExecutionPlan executionPlan,
        int actualqlobalQueryNum,
        int globalAbortUBound,
        string localTitle,
        string option,
        string query,
        bool localDoPooling,
        bool keepConnectionOpenLocal,
        string filePathToExport,
        IDatabaseService actualDatabaseService,
        string selectedConnectionName,
        string selectedDatabase,
        Action<string> updateSelectedDatabase,
        int currentSqlPositionInEditor)
    {
        await Task.Run(async () =>
        {
            _sqlExecutionErrorStore.Clear();
            DbConnection con = bridge.GetConToGo(localDoPooling, keepConnectionOpenLocal, actualDatabaseService);
            bridge.ResetReconnectCounter();

            try
            {
                await SetupConnectionAsync();
                bridge.ClosePreviousResultsIfNeeded();

                for (int currentLocalSqlNumber = 0; currentLocalSqlNumber < executionPlan.SqlStatements.Count; currentLocalSqlNumber++)
                {
                    bridge.ShowProgress(currentLocalSqlNumber, executionPlan.SqlStatements.Count);
                    string sql = executionPlan.SqlStatements[currentLocalSqlNumber];

                    if (ShouldSkipStatement(sql))
                    {
                        continue;
                    }

                    if (await TryHandleSpecialCommandAsync(sql, currentLocalSqlNumber))
                    {
                        continue;
                    }

                    await ExecuteSingleStatementAsync(sql, currentLocalSqlNumber);
                }

                FinalizeExecution();
            }
            catch (Exception ex)
            {
                _sqlExecutionErrorStore.Record(ex, localTitle, selectedConnectionName, selectedDatabase);
                bridge.AddLogMessage(ex.Message, LogMessageType.error, DateTime.Now, localTitle);
            }
            finally
            {
                if (!keepConnectionOpenLocal)
                {
                    con.Close();
                }
            }

            // --- Local functions ---

            async Task SetupConnectionAsync()
            {
                try
                {
                    var res = actualDatabaseService.ChangeDatabaseIfNeeded(con, selectedDatabase);
                    if (!string.IsNullOrWhiteSpace(res))
                    {
                        updateSelectedDatabase(res);
                        selectedDatabase = res;
                    }
                }
                catch (Exception ex)
                {
                    _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                }

                con = SqlDocumentViewModelHelper.OpenConnectionIfNeeded(actualDatabaseService, con, _simpleLogger);

                actualDatabaseService.DbMessageAction += o =>
                {
            if (o?.StartsWith("QUERY PLAN:", StringComparison.Ordinal) == true)
                    {
                        _messageForUserTools.ShowSimpleMessageBoxInstance(o);
                    }
                    else
                    {
                        bridge.AddLogMessage(o ?? "", LogMessageType.ok, DateTime.Now, localTitle);
                    }
                };
            }

            static bool ShouldSkipStatement(string sql)
            {
                return string.IsNullOrWhiteSpace(sql) || sql.IsAllSqlComment();
            }

            async Task<bool> TryHandleSpecialCommandAsync(string sql, int currentLocalSqlNumber)
            {
                var m1 = SqlDocumentViewModelHelper.RxSessionVariableDefine.Match(sql);
                if (m1.Success)
                {
                    await _sqlVariableProcessor.AddSessionVariableAsync(m1, con, localTitle, actualDatabaseService, selectedConnectionName);
                    await Task.Delay(20);
                    return true;
                }

                sql = SasMacroPreprocessor.Expand(sql);
                sql = _sqlVariableProcessor.ReplaceSessionVariables(sql);

                var m = SqlDocumentViewModelHelper.SleepRegex.Match(sql);
                if (m.Success && int.TryParse(m.Groups["num"].Value, out var time))
                {
                    await Task.Delay(time);
                    return true;
                }

                m = SqlDocumentViewModelHelper.ExtractRegex.Match(sql);
                if (m.Success)
                {
                    await AdHocCompressionHelper.Extract(m.Groups["path"].Value, (c, t) => bridge.ShowProgress((int)c, (int)t));
                    return true;
                }

                m = SqlDocumentViewModelHelper.CompressRegex.Match(sql);
                if (m.Success)
                {
                    await AdHocCompressionHelper.Compress(m.Groups["path"].Value, m.Groups["mode"].Value, (c, t) => bridge.ShowProgress((int)c, (int)t));
                    return true;
                }

                var connectionChangeMatch = SqlDocumentViewModelHelper.ChangeConnectionRegex.Match(sql);
                if (connectionChangeMatch.Success)
                {
                    await SwitchConnectionAsync(connectionChangeMatch.Groups["connectionName"].Value);
                    return true;
                }

                return false;
            }

            async Task SwitchConnectionAsync(string connectionToSwitch)
            {
                int index = SqlDocumentViewModelHelper.ConnectionsList.Select(o => o.Name).ToList().IndexOf(connectionToSwitch);
                if (index == -1)
                {
                    return;
                }

                con.Close();
                selectedConnectionName = connectionToSwitch;
                actualDatabaseService = await Task.Run(() => _databaseServiceResolver.GetDatabaseService(_generalApplicationData, selectedConnectionName, delayCache: true));
                con = bridge.GetConToGo(localDoPooling, keepConnectionOpenLocal, actualDatabaseService);
                if (con.State != ConnectionState.Open)
                {
                    con.Open();
                }
                bridge.RefreshDatabaseList(actualDatabaseService);
            }

            async Task ExecuteSingleStatementAsync(string sql, int currentLocalSqlNumber)
            {
                bool retriedAfterReconnect = false;
                var statementSw = Stopwatch.StartNew();
                try
                {
                    while (true)
                    {
                        using var cmd = con.CreateCommand();

                        bridge.TrackQueryState(actualqlobalQueryNum, cmd, SqlCommandState.created);
                        SetTimeoutForCommand(bridge, localTitle, actualDatabaseService, cmd, executionPlan.ForcedTimeout);

                        cmd.CommandText = sql;

                        if (actualqlobalQueryNum < globalAbortUBound)
                        {
                            return;
                        }

                        try
                        {
                            var result = await PrepareAndExecuteReaderAsync(cmd, sql);
                            if (result is null)
                            {
                                // Abort after ExecuteReader — record cancelled with elapsed time.
                                bridge.AddToHistory(
                                    actualDatabaseService.Name,
                                    con.Database,
                                    cmd.CommandText,
                                    HistoryRunStatus.Cancelled,
                                    statementSw.ElapsedMilliseconds,
                                    errorMessage: null);
                                return;
                            }

                            var (rdr, forceAnotherOption, inlineExportMatch, exportUpFrontRowCount) = result.Value;
                            int len = cmd.CommandText.Length;
                            len = Math.Min(len, 100);
                            string shortQuery = cmd.CommandText[..len].Trim().ReplaceLineEndings(" ");
                            bridge.AddLogMessage($"started {shortQuery}", LogMessageType.ok, DateTime.Now, localTitle);

                            await HandleReaderResultsAsync(rdr, cmd, shortQuery, sql, currentLocalSqlNumber, forceAnotherOption, inlineExportMatch, exportUpFrontRowCount);

                            bridge.AddLogMessage($"finished [{cmd.CommandText[..len].Trim().Replace('\n', ' ').Replace('\r', ' ')} ...]", LogMessageType.ok, DateTime.Now, localTitle);
                            bridge.AddToHistory(
                                actualDatabaseService.Name,
                                con.Database,
                                cmd.CommandText,
                                HistoryRunStatus.Success,
                                statementSw.ElapsedMilliseconds,
                                errorMessage: null);
                            return;
                        }
                        catch (Exception exx1)
                        {
                            bool isCancelled = actualqlobalQueryNum < globalAbortUBound
                                || (exx1.Message?.StartsWith("ERROR: Query was cancelled", StringComparison.Ordinal) == true);

                            if (!retriedAfterReconnect
                                && !isCancelled
                                && ConnectionRecoveryPolicy.IsBrokenConnection(exx1, con.State))
                            {
                                var recovered = bridge.TryReconnectOnce(
                                    actualDatabaseService,
                                    con,
                                    localDoPooling,
                                    keepConnectionOpenLocal,
                                    isCancelled: false,
                                    exx1);
                                if (recovered is not null)
                                {
                                    con = recovered;
                                    try
                                    {
                                        if (con.State != ConnectionState.Open)
                                        {
                                            con.Open();
                                        }
                                    }
                                    catch (Exception openEx)
                                    {
                                        _simpleLogger.TrackError(openEx, isCrash: false);
                                        HandleStatementError(openEx, cmd, sql, currentLocalSqlNumber);
                                        bridge.AddToHistory(
                                            actualDatabaseService.Name,
                                            con.Database,
                                            cmd.CommandText,
                                            HistoryRunStatus.Failed,
                                            statementSw.ElapsedMilliseconds,
                                            openEx.Message);
                                        return;
                                    }

                                    bridge.AddLogMessage("Auto-recovery: reconnected once, retrying statement.", LogMessageType.ok, DateTime.Now, localTitle);
                                    retriedAfterReconnect = true;
                                    continue;
                                }
                            }

                            HandleStatementError(exx1, cmd, sql, currentLocalSqlNumber);
                            bridge.AddToHistory(
                                actualDatabaseService.Name,
                                con.Database,
                                cmd.CommandText,
                                isCancelled ? HistoryRunStatus.Cancelled : HistoryRunStatus.Failed,
                                statementSw.ElapsedMilliseconds,
                                exx1.Message);
                            if (!executionPlan.ContinueOnError && exx1.Message != "ERROR: Query was cancelled.")
                            {
                                return;
                            }

                            return;
                        }
                        finally
                        {
                            bridge.TrackQueryState(actualqlobalQueryNum, cmd, SqlCommandState.finished);
                        }
                    }
                }
                finally
                {
                    currentSqlPositionInEditor += sql.Length + 1;
                }
            }

            async Task<(DbDataReader rdr, string forceAnotherOption, System.Text.RegularExpressions.Match inlineExportMatch, long? exportUpFrontRowCount)?> PrepareAndExecuteReaderAsync(DbCommand cmd, string sql)
            {
                string forceAnotherOption = "";
                long? exportUpFrontRowCount = null;
                var inlineExportMatch = SqlDocumentViewModelHelper.rxExportCsvXlsx.Match(sql);

                if (inlineExportMatch.Success)
                {
                    forceAnotherOption = inlineExportMatch.Groups["exportName"].Value;
                    cmd.CommandText = inlineExportMatch.Groups["sql"].Value;
                    filePathToExport = inlineExportMatch.Groups["filePath"].Value;

                    if (inlineExportMatch.Groups["options"].Value.Contains("#upFrontRowsCount true", StringComparison.OrdinalIgnoreCase))
                    {
                        exportUpFrontRowCount = await CountRowsForExportAsync(inlineExportMatch);
                    }
                }

                bridge.TrackQueryState(actualqlobalQueryNum, cmd, SqlCommandState.started);

                CommandBehavior cb = CommandBehavior.SequentialAccess;
            if (option.StartsWith(".csv", StringComparison.Ordinal))
                {
                    cb = CommandBehavior.Default;
                }

                var rdr = cmd.ExecuteReader(cb);
                bridge.TrackQueryState(actualqlobalQueryNum, cmd, SqlCommandState.executed);

                if (actualqlobalQueryNum < globalAbortUBound)
                {
                    rdr.Dispose();
                    return null;
                }

                return (rdr, forceAnotherOption, inlineExportMatch, exportUpFrontRowCount);
            }

            async Task<long?> CountRowsForExportAsync(System.Text.RegularExpressions.Match inlineExportMatch)
            {
                try
                {
                    using var cmdX = con.CreateCommand();
                    cmdX.CommandTimeout = 60;
                    cmdX.CommandText = $"SELECT COUNT(1) FROM ({inlineExportMatch.Groups["sql"].Value}) TMP";
                    var count = cmdX.ExecuteScalar() as long?;
                    bridge.AddLogMessage($"{count:N0} rows to export..", LogMessageType.ok, DateTime.Now, localTitle);
                    bridge.AddLogMessage($"command timeout is set to {new TimeSpan(0, 0, cmdX.CommandTimeout):g}", LogMessageType.ok, DateTime.Now, localTitle);
                    return count;
                }
                catch (Exception ex)
                {
                    _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                    return null;
                }
            }

            async Task HandleReaderResultsAsync(DbDataReader rdr, DbCommand cmd, string shortQuery, string sql, int currentLocalSqlNumber, string forceAnotherOption, System.Text.RegularExpressions.Match inlineExportMatch, long? exportUpFrontRowCount)
            {
                while (true)
                {
            if (string.IsNullOrEmpty(forceAnotherOption) && option.StartsWith("Grid", StringComparison.Ordinal) && rdr.FieldCount > 0)
                    {
                        bridge.HandleStandardGrid(actualDatabaseService, $"{localTitle}_{currentLocalSqlNumber}", query, null, executionPlan.TabsWithRows, actualqlobalQueryNum, rdr, cmd, shortQuery);
                    }
            else if ((forceAnotherOption == "@expXlsx" || option.StartsWith(".xlsb", StringComparison.Ordinal)) && !string.IsNullOrWhiteSpace(filePathToExport))
                    {
                        await HandleExcelExportAsync(rdr, sql);
                    }
            else if ((forceAnotherOption == "@expCsv" || option.Contains(".csv", StringComparison.Ordinal) || option.StartsWith(".parquet", StringComparison.Ordinal)) && !string.IsNullOrWhiteSpace(filePathToExport))
                    {
                        await HandleCsvParquetExportAsync(rdr, inlineExportMatch, exportUpFrontRowCount);
                    }

                    if (rdr.RecordsAffected != -1)
                    {
                        bridge.HandleAnotherResult(null, rdr);
                    }

                    if (actualqlobalQueryNum < globalAbortUBound)
                    {
                        break;
                    }

                    if (!rdr.NextResult())
                    {
                        break;
                    }
                }
            }

            Task HandleExcelExportAsync(DbDataReader rdr, string sql)
            {
                var timestamp = Stopwatch.GetTimestamp();

                void ProgressAction(int n)
                {
                    _messageForUserTools.DispatcherActionInstance(() =>
                    {
                        if (Stopwatch.GetElapsedTime(timestamp).Seconds >= 10)
                        {
                            bridge.AddLogMessage($"Exporting... {n:N0}", LogMessageType.ok, DateTime.Now, localTitle);
                            timestamp = Stopwatch.GetTimestamp();
                        }
                    });
                }

                rdr.HandleExcelOutput(filePathToExport, sql, "Justy", ProgressAction);
                return Task.CompletedTask;
            }

            async Task HandleCsvParquetExportAsync(DbDataReader rdr, System.Text.RegularExpressions.Match inlineExportMatch, long? exportUpFrontRowCount)
            {
                AdvancedExportOptions? opt = null;
                if (inlineExportMatch.Success)
                {
                    opt = AdvancedExportOptions.ParseFromString(inlineExportMatch.Groups["options"].Value);
                }

                Stopwatch sw = Stopwatch.StartNew();

                void InnerAction(long localN)
                {
                    if (exportUpFrontRowCount is long longRows)
                    {
                        bridge.AddLogMessage($" Exporting...  {((double)localN / longRows):P1}", LogMessageType.ok, DateTime.Now, localTitle);
                        bridge.AddLogMessage($" {(1_000 * localN / sw.Elapsed.TotalMilliseconds):N0} rows per sec", LogMessageType.ok, DateTime.Now, localTitle);
                        if (localN > 0)
                        {
                            long elapsedTics = (long)(((double)(longRows - localN) / localN) * sw.Elapsed.Ticks);
                            bridge.AddLogMessage($" {new TimeSpan(elapsedTics):g} to finish", LogMessageType.ok, DateTime.Now, localTitle);
                        }
                        bridge.ShowProgress((int)(100 * localN / longRows), 100);
                    }
                    else
                    {
                        bridge.AddLogMessage($"Exporting...  {localN:N0}", LogMessageType.ok, DateTime.Now, localTitle);
                        bridge.AddLogMessage($" {(1_000 * localN / sw.Elapsed.TotalMilliseconds):N0} rows per sec", LogMessageType.ok, DateTime.Now, localTitle);
                    }
                }

                void ProgressAction2(long n)
                {
                    long localN = n;
                    _messageForUserTools.DispatcherActionInstance(() => InnerAction(localN));
                }

                await rdr.HandleCsvOrParquetOutput(filePathToExport, opt, ProgressAction2).ConfigureAwait(false);
            }

            void HandleStatementError(Exception exx1, DbCommand cmd, string sql, int currentLocalSqlNumber)
            {
                _sqlExecutionErrorStore.Record(exx1, localTitle, selectedConnectionName, selectedDatabase);
                if (exx1.Message is not null
                    && exx1.Message != "ERROR: Transaction rolled back by client"
                && !exx1.Message.StartsWith("ERROR: Query was cancelled", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("ERROR: 15 : Header precompile failed.", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("ERROR: relation does not exist", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("ERROR [42704] [IBM][DB2/NT64]", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("ERROR [42000]", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("ERROR: Attribute ", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("A timeout has occured. If you were establishing a connection", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("The CommandText to be set should not be null or Empty!", StringComparison.Ordinal)
                && !exx1.Message.StartsWith("ERROR:  relation does not exist", StringComparison.Ordinal)
                    )
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance(exx1);
                }

                if ((actualDatabaseService.DatabaseType == DatabaseTypeEnum.NetezzaSQL || actualDatabaseService.DatabaseType == DatabaseTypeEnum.NetezzaSQLOdbc)
                && con is not null && exx1.Message.StartsWith("A timeout has occured. If you were establishing a connection", StringComparison.Ordinal))
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance("Due to NPS driver limitation connection have to be reopened", "Error");
                    con.Close();
                    con.Open();
                }

                if (exx1.Message == "ERROR: Query was cancelled.")
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance(exx1.Message, "Error");
                }
                else
                {
                    var (position, length) = actualDatabaseService.HandleExceptions(sql, exx1);
                    if (position != -1)
                    {
                        // HandleExceptions returns a 0-based offset within the executed statement.
                        bridge.SelectError(currentSqlPositionInEditor + position, length);
                    }
                }

                bridge.ErrorMessageToUi(localTitle, null, actualqlobalQueryNum, actualDatabaseService, currentLocalSqlNumber, sql, cmd, exx1);
            }

            void FinalizeExecution()
            {
                bridge.ShowProgress(executionPlan.SqlStatements.Count, executionPlan.SqlStatements.Count);

                try
                {
                    bridge.FlashWindowExIfNeeded();
                }
                catch (Exception ex)
                {
                    _simpleLogger.TrackError(ex, isCrash: false);
                }
            }
        });
    }

    private void SetTimeoutForCommand(ISqlExecutionBridge bridge, string localTitle, IDatabaseService service, DbCommand cmd, int? forcedTimeout = null)
    {
        if ((service.DatabaseType == DatabaseTypeEnum.NetezzaSQL || service.DatabaseType == DatabaseTypeEnum.NetezzaSQLOdbc) && !OperatingSystem.IsWindows())
        {
            bridge.AddLogMessage("TO DO CommandTimeout on nonWindows", LogMessageType.ok, System.DateTime.Now, localTitle);
        }
        else
        {
            if (forcedTimeout is not null)
            {
                cmd.CommandTimeout = (int)forcedTimeout;
            }
            else if (service.DatabaseType == DatabaseTypeEnum.NetezzaSQL || service.DatabaseType == DatabaseTypeEnum.NetezzaSQLOdbc)
            {
#pragma warning disable CA5394 // Timeout jitter is a scheduling detail, not security randomness.
                int random = Random.Shared.Next(0, (int)(_generalApplicationData.Config.CommandTimeout * 0.05));
#pragma warning restore CA5394
                cmd.CommandTimeout = _generalApplicationData.Config.CommandTimeout + random;
            }
            else
            {
                cmd.CommandTimeout = _generalApplicationData.Config.CommandTimeout;
            }
        }
    }
}
