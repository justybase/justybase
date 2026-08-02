namespace JustyBase.Services.Documents;

public interface ISqlDocumentExecutionServices
{
    ISqlExecutionService ExecutionService { get; }
    ISqlConnectionManager ConnectionManager { get; }
    ISqlResultDispatcherService ResultDispatcherService { get; }
    ISqlExecutionStateService ExecutionStateService { get; }
    IDatabaseListSyncService DatabaseListSyncService { get; }
    ISqlRunPreparationService RunPreparationService { get; }
    ISqlRunLifecycleService RunLifecycleService { get; }
    ISqlRunOrchestrationService RunOrchestrationService { get; }
}

public sealed class SqlDocumentExecutionServices : ISqlDocumentExecutionServices
{
    public ISqlExecutionService ExecutionService { get; }
    public ISqlConnectionManager ConnectionManager { get; }
    public ISqlResultDispatcherService ResultDispatcherService { get; }
    public ISqlExecutionStateService ExecutionStateService { get; }
    public IDatabaseListSyncService DatabaseListSyncService { get; }
    public ISqlRunPreparationService RunPreparationService { get; }
    public ISqlRunLifecycleService RunLifecycleService { get; }
    public ISqlRunOrchestrationService RunOrchestrationService { get; }

    public SqlDocumentExecutionServices(
        ISqlExecutionService executionService,
        ISqlConnectionManager connectionManager,
        ISqlResultDispatcherService resultDispatcherService,
        ISqlExecutionStateService executionStateService,
        IDatabaseListSyncService databaseListSyncService,
        ISqlRunPreparationService runPreparationService,
        ISqlRunLifecycleService runLifecycleService,
        ISqlRunOrchestrationService runOrchestrationService)
    {
        ExecutionService = executionService;
        ConnectionManager = connectionManager;
        ResultDispatcherService = resultDispatcherService;
        ExecutionStateService = executionStateService;
        DatabaseListSyncService = databaseListSyncService;
        RunPreparationService = runPreparationService;
        RunLifecycleService = runLifecycleService;
        RunOrchestrationService = runOrchestrationService;
    }
}
