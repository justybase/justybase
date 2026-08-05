using JustyBase.Common.Contracts;
using JustyBase.Helpers.Shared;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.Documents;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlRunOrchestrationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDatabaseServiceIsMissing_DispatchesWarningAndSkipsExecution()
    {
        var runPreparationService = new Mock<ISqlRunPreparationService>();
        runPreparationService
            .Setup(x => x.InitializeDatabaseServiceAsync("main", It.IsAny<Func<Task>>()))
            .ReturnsAsync((IDatabaseService?)null);

        var executionService = new Mock<ISqlExecutionService>(MockBehavior.Strict);
        var executionStateService = new Mock<ISqlExecutionStateService>();
        executionStateService.SetupProperty(x => x.GlobalAbortUpperBound, 0);

        var resultDispatcherService = new Mock<ISqlResultDispatcherService>();
        var request = CreateRequest();
        var sut = CreateService(
            runPreparationService,
            executionService,
            executionStateService,
            resultDispatcherService);

        var result = await sut.ExecuteAsync(request, () => Task.CompletedTask);

        Assert.Equal(SqlRunOrchestrationStatus.MissingConnection, result.Status);
        resultDispatcherService.Verify(
            x => x.DispatchWarningResult(
                request.InstanceId,
                request.ResultManager,
                request.ExecutionSettings.LocalTitle,
                request.GlobalQueryNumber,
                ref It.Ref<int>.IsAny,
                null),
            Times.Once);
        executionService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDatabaseServiceIsAvailable_SynchronizesDatabasesAndExecutesQuery()
    {
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabases("")).Returns(["MAIN"]);

        var runPreparationService = new Mock<ISqlRunPreparationService>();
        runPreparationService
            .Setup(x => x.InitializeDatabaseServiceAsync("main", It.IsAny<Func<Task>>()))
            .ReturnsAsync(databaseService.Object);

        var executionStateService = new Mock<ISqlExecutionStateService>();
        executionStateService.SetupProperty(x => x.GlobalAbortUpperBound, 0);

        string? selectedDatabasePassedToExecution = null;
        var executionService = new Mock<ISqlExecutionService>();
        executionService
            .Setup(x => x.ExecuteSqlAsync(
                It.IsAny<ISqlExecutionBridge>(),
                It.IsAny<SqlDocumentViewModelHelper.SqlExecutionPlan>(),
                5,
                0,
                "Run 1",
                "Grid",
                "SELECT 1",
                false,
                false,
                string.Empty,
                databaseService.Object,
                "main",
                It.IsAny<string>(),
                It.IsAny<Action<string>>(),
                17))
            .Callback<ISqlExecutionBridge, SqlDocumentViewModelHelper.SqlExecutionPlan, int, int, string, string, string, bool, bool, string, IDatabaseService, string, string, Action<string>, int>(
                (_, _, _, _, _, _, _, _, _, _, _, _, selectedDatabase, _, _) => selectedDatabasePassedToExecution = selectedDatabase)
            .Returns(Task.CompletedTask);

        var addedDatabases = new List<string>();
        string? selectedDatabase = null;
        var request = CreateRequest(
            selectedDatabase: string.Empty,
            currentDatabases: [],
            addDatabase: addedDatabases.Add,
            updateSelectedDatabase: databaseName => selectedDatabase = databaseName);
        var sut = CreateService(
            runPreparationService,
            executionService,
            executionStateService);

        var result = await sut.ExecuteAsync(request, () => Task.CompletedTask);

        Assert.Equal(SqlRunOrchestrationStatus.Completed, result.Status);
        Assert.Equal(["MAIN"], addedDatabases);
        Assert.Equal("MAIN", selectedDatabase);
        Assert.Equal("MAIN", selectedDatabasePassedToExecution);
        executionService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutionThrows_DispatchesRuntimeErrorAndReturnsFailed()
    {
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabases("")).Returns([]);

        var runPreparationService = new Mock<ISqlRunPreparationService>();
        runPreparationService
            .Setup(x => x.InitializeDatabaseServiceAsync("main", It.IsAny<Func<Task>>()))
            .ReturnsAsync(databaseService.Object);

        var executionStateService = new Mock<ISqlExecutionStateService>();
        executionStateService.SetupProperty(x => x.GlobalAbortUpperBound, 0);

        var exception = new InvalidOperationException("boom");
        var executionService = new Mock<ISqlExecutionService>();
        executionService
            .Setup(x => x.ExecuteSqlAsync(
                It.IsAny<ISqlExecutionBridge>(),
                It.IsAny<SqlDocumentViewModelHelper.SqlExecutionPlan>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<IDatabaseService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<string>>(),
                It.IsAny<int>()))
            .ThrowsAsync(exception);

        var resultDispatcherService = new Mock<ISqlResultDispatcherService>();
        var simpleLogger = new Mock<ISimpleLogger>();
        var messageForUserTools = new Mock<IMessageForUserTools>();
        var request = CreateRequest();
        var sut = CreateService(
            runPreparationService,
            executionService,
            executionStateService,
            resultDispatcherService,
            simpleLogger: simpleLogger,
            messageForUserTools: messageForUserTools);

        var result = await sut.ExecuteAsync(request, () => Task.CompletedTask);

        Assert.Equal(SqlRunOrchestrationStatus.Failed, result.Status);
        simpleLogger.Verify(x => x.TrackError(exception, false), Times.Once);
        messageForUserTools.Verify(x => x.ShowSimpleMessageBoxInstance(exception), Times.Once);
        resultDispatcherService.Verify(
            x => x.DispatchRuntimeErrorResult(
                request.InstanceId,
                request.ResultManager,
                request.ExecutionSettings.LocalTitle,
                request.GlobalQueryNumber,
                ref It.Ref<int>.IsAny,
                exception),
            Times.Once);
    }

    private static SqlRunOrchestrationService CreateService(
        Mock<ISqlRunPreparationService>? runPreparationService = null,
        Mock<ISqlExecutionService>? executionService = null,
        Mock<ISqlExecutionStateService>? executionStateService = null,
        Mock<ISqlResultDispatcherService>? resultDispatcherService = null,
        Mock<ISimpleLogger>? simpleLogger = null,
        Mock<IMessageForUserTools>? messageForUserTools = null)
    {
        return new SqlRunOrchestrationService(
            (runPreparationService ?? new Mock<ISqlRunPreparationService>()).Object,
            (executionService ?? new Mock<ISqlExecutionService>()).Object,
            (executionStateService ?? new Mock<ISqlExecutionStateService>()).Object,
            (resultDispatcherService ?? new Mock<ISqlResultDispatcherService>()).Object,
            new DatabaseListSyncService(),
            new SqlRunLifecycleService(),
            (simpleLogger ?? new Mock<ISimpleLogger>()).Object,
            (messageForUserTools ?? new Mock<IMessageForUserTools>()).Object);
    }

    private static SqlRunOrchestrationRequest CreateRequest(
        string selectedDatabase = "MAIN",
        IReadOnlyCollection<string>? currentDatabases = null,
        Action<string>? addDatabase = null,
        Action<string>? updateSelectedDatabase = null)
    {
        return new SqlRunOrchestrationRequest(
            InstanceId: "doc-1",
            ResultManager: Mock.Of<ISqlResultManager>(),
            ExecutionBridge: Mock.Of<ISqlExecutionBridge>(),
            ExecutionSettings: new SqlRunExecutionSettings("Run 1", false, false, false),
            ExecutionPlan: new SqlDocumentViewModelHelper.SqlExecutionPlan(
                SingleCommand: false,
                TabsWithRows: true,
                TimeoutOverride: false,
                ForcedTimeout: 30,
                ContinueOnError: false,
                SqlStatements: ["SELECT 1"]),
            GlobalQueryNumber: 5,
            Option: "Grid",
            Query: "SELECT 1",
            FilePathToExport: string.Empty,
            SelectedConnectionName: "main",
            SelectedDatabase: selectedDatabase,
            CurrentSqlPositionInEditor: 17,
            CurrentDatabases: currentDatabases ?? ["ARCHIVE"],
            AddDatabase: addDatabase ?? (_ => { }),
            UpdateSelectedDatabase: updateSelectedDatabase ?? (_ => { }),
            CurrentLogMessage: null);
    }
}
