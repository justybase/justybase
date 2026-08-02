using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Helpers;
using JustyBase.Helpers.Shared;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services;

namespace JustyBase.Services.Documents;

public enum SqlRunOrchestrationStatus
{
    Completed,
    MissingConnection,
    Cancelled,
    Failed,
    Skipped
}

public sealed record SqlRunOrchestrationRequest(
    string InstanceId,
    ISqlResultManager ResultManager,
    ISqlExecutionBridge ExecutionBridge,
    SqlRunExecutionSettings ExecutionSettings,
    SqlDocumentViewModelHelper.SqlExecutionPlan ExecutionPlan,
    int GlobalQueryNumber,
    string Option,
    string Query,
    string FilePathToExport,
    string SelectedConnectionName,
    string SelectedDatabase,
    int CurrentSqlPositionInEditor,
    IReadOnlyCollection<string> CurrentDatabases,
    Action<string> AddDatabase,
    Action<string> UpdateSelectedDatabase,
    LogMessage? CurrentLogMessage);

public sealed record SqlRunOrchestrationResult(SqlRunOrchestrationStatus Status);

public interface ISqlRunOrchestrationService
{
    Task<SqlRunOrchestrationResult> ExecuteAsync(SqlRunOrchestrationRequest request, Func<Task> loadPluginsIfNeededAsync);
}

public sealed class SqlRunOrchestrationService : ISqlRunOrchestrationService
{
    private readonly ISqlRunPreparationService _runPreparationService;
    private readonly ISqlExecutionService _executionService;
    private readonly ISqlExecutionStateService _executionStateService;
    private readonly ISqlResultDispatcherService _resultDispatcherService;
    private readonly IDatabaseListSyncService _databaseListSyncService;
    private readonly ISqlRunLifecycleService _runLifecycleService;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IMessageForUserTools _messageForUserTools;

    public SqlRunOrchestrationService(
        ISqlRunPreparationService runPreparationService,
        ISqlExecutionService executionService,
        ISqlExecutionStateService executionStateService,
        ISqlResultDispatcherService resultDispatcherService,
        IDatabaseListSyncService databaseListSyncService,
        ISqlRunLifecycleService runLifecycleService,
        ISimpleLogger simpleLogger,
        IMessageForUserTools messageForUserTools)
    {
        _runPreparationService = runPreparationService;
        _executionService = executionService;
        _executionStateService = executionStateService;
        _resultDispatcherService = resultDispatcherService;
        _databaseListSyncService = databaseListSyncService;
        _runLifecycleService = runLifecycleService;
        _simpleLogger = simpleLogger;
        _messageForUserTools = messageForUserTools;
    }

    public async Task<SqlRunOrchestrationResult> ExecuteAsync(SqlRunOrchestrationRequest request, Func<Task> loadPluginsIfNeededAsync)
    {
        try
        {
            IDatabaseService? actualDatabaseService = await _runPreparationService.InitializeDatabaseServiceAsync(
                request.SelectedConnectionName,
                loadPluginsIfNeededAsync);

            if (actualDatabaseService is null)
            {
                HandleMissingConnection(request);
                return new(SqlRunOrchestrationStatus.MissingConnection);
            }

            // The orchestration layer owns the selected, resolved connection.
            // Running the shared policy here avoids stale/null VM service state.
            string? riskDriver = IsNetezza(actualDatabaseService)
                ? "NetezzaSQL"
                : null;
            var risks = new JustyBase.Core.Risk.SqlRiskAnalysisService().Analyze(request.Query, riskDriver);
            if (risks.Count > 0)
            {
                string warning = string.Join(Environment.NewLine, risks.Select(risk => $"• {risk.Message}"));
                bool confirmed = await _messageForUserTools.ShowConfirmationDialogAsync(
                    warning,
                    "SQL risk confirmation");
                if (!confirmed)
                    return new(SqlRunOrchestrationStatus.Skipped);
            }

            string selectedDatabase = request.SelectedDatabase;

            void UpdateSelectedDatabase(string databaseName)
            {
                selectedDatabase = databaseName;
                request.UpdateSelectedDatabase(databaseName);
            }

            SyncDatabaseList(actualDatabaseService, request, UpdateSelectedDatabase);

            if (request.GlobalQueryNumber < _executionStateService.GlobalAbortUpperBound)
            {
                return new(SqlRunOrchestrationStatus.Skipped);
            }

            await _executionService.ExecuteSqlAsync(
                request.ExecutionBridge,
                request.ExecutionPlan,
                request.GlobalQueryNumber,
                _executionStateService.GlobalAbortUpperBound,
                request.ExecutionSettings.LocalTitle,
                request.Option,
                request.Query,
                request.ExecutionSettings.DoPooling,
                request.ExecutionSettings.KeepConnectionOpen,
                request.FilePathToExport,
                actualDatabaseService,
                request.SelectedConnectionName,
                selectedDatabase,
                UpdateSelectedDatabase,
                request.CurrentSqlPositionInEditor);

            return new(SqlRunOrchestrationStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            return new(SqlRunOrchestrationStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _simpleLogger.LogAndShowError(ex, _messageForUserTools);

            int abortUpperBound = _executionStateService.GlobalAbortUpperBound;
            _resultDispatcherService.DispatchRuntimeErrorResult(
                request.InstanceId,
                request.ResultManager,
                request.ExecutionSettings.LocalTitle,
                request.GlobalQueryNumber,
                ref abortUpperBound,
                ex);
            _executionStateService.GlobalAbortUpperBound = abortUpperBound;

            return new(SqlRunOrchestrationStatus.Failed);
        }
    }

    private void HandleMissingConnection(SqlRunOrchestrationRequest request)
    {
        int abortUpperBound = _executionStateService.GlobalAbortUpperBound;
        _resultDispatcherService.DispatchWarningResult(
            request.InstanceId,
            request.ResultManager,
            request.ExecutionSettings.LocalTitle,
            request.GlobalQueryNumber,
            ref abortUpperBound,
            null);
        _executionStateService.GlobalAbortUpperBound = abortUpperBound;

        if (request.CurrentLogMessage is null)
        {
            return;
        }

        var missingConnectionPlan = _runLifecycleService.CreateMissingConnectionPlan();
        request.CurrentLogMessage.AddInnerMessageInUiThread(missingConnectionPlan.InnerLogMessage, DateTime.Now);
    }

    private static bool IsNetezza(IDatabaseService service)
        => service.DatabaseType is DatabaseTypeEnum.NetezzaSQL or DatabaseTypeEnum.NetezzaSQLOdbc
           || service.GetType().Name.Contains("Netezza", StringComparison.OrdinalIgnoreCase);

    private void SyncDatabaseList(IDatabaseService actualDatabaseService, SqlRunOrchestrationRequest request, Action<string> updateSelectedDatabase)
    {
        var syncPlan = _databaseListSyncService.BuildSyncPlan(
            actualDatabaseService.GetDatabases(""),
            request.CurrentDatabases,
            request.SelectedDatabase);

        foreach (var databaseName in syncPlan.DatabasesToAdd)
        {
            request.AddDatabase(databaseName);
        }

        if (!string.IsNullOrWhiteSpace(syncPlan.UpdatedSelectedDatabase)
            && !string.Equals(request.SelectedDatabase, syncPlan.UpdatedSelectedDatabase, StringComparison.Ordinal))
        {
            updateSelectedDatabase(syncPlan.UpdatedSelectedDatabase);
        }
    }
}
