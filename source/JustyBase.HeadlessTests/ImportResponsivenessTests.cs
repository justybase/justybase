using Avalonia.Threading;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services.Documents;
using JustyBase.Services;
using JustyBase.ViewModels.Documents;
using Moq;
using Xunit.Abstractions;

namespace JustyBase.HeadlessTests;

public sealed class ImportResponsivenessTests : HeadlessSessionTestBase
{
    private readonly ITestOutputHelper _output;

    public ImportResponsivenessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Import_ConnectionAndDatabaseSelection_WithDelayedDb_StaysWithinStallBudget() => RunOnUi(() =>
    {
        const string connectionName = "IMPORT_RESP_CONNECTION";
        const int injectedDelayMs = DelayedDatabaseServiceMock.DefaultInjectedDelayMs;

        var appData = HeadlessProductHost.CreateAppData(connectionName);
        var fakeService = DelayedDatabaseServiceMock.Create(
            delayMs: injectedDelayMs,
            tablesPerSchema: 15,
            schemas: 3,
            delayGetDatabases: true,
            delayGetSchemas: true,
            delayGetDbObjects: true);

        var resolver = new Mock<IDatabaseServiceResolver>();
        resolver
            .Setup(r => r.GetDatabaseService(
                It.IsAny<IGeneralApplicationData>(),
                connectionName,
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()))
            .Returns(fakeService.Object);

        var vm = new ImportViewModel(
            Mock.Of<IAvaloniaSpecificHelpers>(),
            appData.Object,
            Mock.Of<IMessageForUserTools>(),
            Mock.Of<IDocumentCloseDecisionService>(),
            Mock.Of<IActiveDocumentManager>(),
            resolver.Object);

        var connection = new ConnectionItem(connectionName, DatabaseTypeEnum.Sqlite)
        {
            DefaultDatabase = "main",
            DatabaseList = ["main"]
        };

        var probe = new UiResponsivenessProbe(_output);
        var snapshot = probe.RunDuring(
            "Import.SelectedConnectionThenDatabase",
            async () =>
            {
                vm.SelectedConnection = connection;
                // Let database list populate, then select database (schemas load).
                var databasesDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (vm.DatabaseItems.Count == 0 && DateTime.UtcNow < databasesDeadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20).ConfigureAwait(true);
                }

                Assert.NotEmpty(vm.DatabaseItems);
                vm.SelectedDatabase = "main";

                var schemasDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (vm.SchemaItems.Count == 0 && DateTime.UtcNow < schemasDeadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20).ConfigureAwait(true);
                }
            },
            TimeSpan.FromSeconds(20),
            injectedDelayMs: injectedDelayMs);

        ResponsivenessMetricsWriter.Append(nameof(Import_ConnectionAndDatabaseSelection_WithDelayedDb_StaysWithinStallBudget), snapshot);
        UiResponsivenessProbe.AssertWithinBudget(snapshot);

        Dispatcher.UIThread.RunJobs();
        Assert.Contains("main", vm.DatabaseItems);
        Assert.NotEmpty(vm.SchemaItems);
    });
}
