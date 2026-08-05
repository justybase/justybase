using Avalonia.Platform.Storage;
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

    [Fact]
    public Task Import_UnrefreshedSchemaCache_ForcesRefreshAndPopulatesDatabaseAndSchemaLists() => RunOnUi(() =>
    {
        const string connectionName = "IMPORT_UNREFRESHED_CONNECTION";

        var appData = HeadlessProductHost.CreateAppData(connectionName);

        // Cached service before any schema refresh: empty lists, cache never populated.
        var unrefreshed = new Mock<IDatabaseService>();
        unrefreshed.SetupGet(s => s.ConnectedLevel).Returns(DatabaseConnectedLevel.Connected);
        unrefreshed.Setup(s => s.GetDatabases(It.IsAny<string>())).Returns([]);
        unrefreshed.Setup(s => s.GetSchemas(It.IsAny<string>(), It.IsAny<string>())).Returns([]);

        // Service after the forced refresh: schema cache is populated.
        var refreshed = new Mock<IDatabaseService>();
        refreshed.SetupGet(s => s.ConnectedLevel).Returns(DatabaseConnectedLevel.ConnectedColumns);
        refreshed.Setup(s => s.GetDatabases(It.IsAny<string>())).Returns(["main"]);
        refreshed.Setup(s => s.GetSchemas("main", It.IsAny<string>())).Returns(["S1", "S2"]);

        var resolver = new Mock<IDatabaseServiceResolver>();
        resolver
            .Setup(r => r.GetDatabaseService(
                It.IsAny<IGeneralApplicationData>(),
                connectionName,
                It.IsAny<bool>(),
                It.Is<bool>(forceRefresh => forceRefresh),
                It.IsAny<Action<string>?>()))
            .Returns(refreshed.Object);
        resolver
            .Setup(r => r.GetDatabaseService(
                It.IsAny<IGeneralApplicationData>(),
                connectionName,
                It.IsAny<bool>(),
                It.Is<bool>(forceRefresh => !forceRefresh),
                It.IsAny<Action<string>?>()))
            .Returns(unrefreshed.Object);

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
            "Import.UnrefreshedSchemaCache",
            async () =>
            {
                vm.SelectedConnection = connection;

                var databasesDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (vm.DatabaseItems.Count == 0 && DateTime.UtcNow < databasesDeadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20).ConfigureAwait(true);
                }

                Assert.Contains("main", vm.DatabaseItems);

                vm.SelectedDatabase = "main";
                var schemasDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (vm.SchemaItems.Count == 0 && DateTime.UtcNow < schemasDeadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20).ConfigureAwait(true);
                }
            },
            TimeSpan.FromSeconds(30));

        ResponsivenessMetricsWriter.Append(nameof(Import_UnrefreshedSchemaCache_ForcesRefreshAndPopulatesDatabaseAndSchemaLists), snapshot);
        UiResponsivenessProbe.AssertWithinBudget(snapshot);

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["S1", "S2"], vm.SchemaItems.ToArray());

        // The unrefreshed cache must have been recovered via a forced refresh.
        resolver.Verify(r => r.GetDatabaseService(
            It.IsAny<IGeneralApplicationData>(),
            connectionName,
            It.IsAny<bool>(),
            It.Is<bool>(forceRefresh => forceRefresh),
            It.IsAny<Action<string>?>()), Times.AtLeastOnce);
    });

    [Fact]
    public Task Import_FileOpenedAndValidated_EnablesStartButtons() => RunOnUi(() =>
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_start_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csv, "id,price\n1,10.5\n2,20.75\n3,1\n");

            var appData = HeadlessProductHost.CreateAppData("IMPORT_START_CONNECTION");

            var file = new Mock<IStorageFile>();
            file.Setup(f => f.Path).Returns(new Uri(Path.GetFullPath(csv)));
            var storage = new Mock<IStorageProvider>();
            storage.Setup(s => s.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
                .ReturnsAsync((IReadOnlyList<IStorageFile>)[file.Object]);
            var helpers = new Mock<IAvaloniaSpecificHelpers>();
            helpers.Setup(h => h.GetStorageProvider()).Returns(storage.Object);

            // The real implementation dispatches the state-machine completion back to the UI
            // thread; the mock must run it inline so the flags reset while the pump runs.
            var messageForUserTools = new Mock<IMessageForUserTools>();
            messageForUserTools
                .Setup(m => m.DispatcherActionInstance(It.IsAny<Action>()))
                .Callback<Action>(action => action());

            var resolver = new Mock<IDatabaseServiceResolver>();
            resolver
                .Setup(r => r.GetDatabaseService(
                    It.IsAny<IGeneralApplicationData>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<Action<string>?>()))
                .Returns(HeadlessProductHost.CreateLargeSchemaService(tablesPerSchema: 2, schemas: 2).Object);

            var vm = new ImportViewModel(
                helpers.Object,
                appData.Object,
                messageForUserTools.Object,
                Mock.Of<IDocumentCloseDecisionService>(),
                Mock.Of<IActiveDocumentManager>(),
                resolver.Object);

            var probe = new UiResponsivenessProbe(_output);
            var snapshot = probe.RunDuring(
                "Import.OpenFileThenStartEnabled",
                async () =>
                {
                    vm.OpenFileForImportCommand.Execute(null);

                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                    while (!vm.StartEnabled && DateTime.UtcNow < deadline)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(20).ConfigureAwait(true);
                    }
                },
                TimeSpan.FromSeconds(30));

            ResponsivenessMetricsWriter.Append(nameof(Import_FileOpenedAndValidated_EnablesStartButtons), snapshot);
            UiResponsivenessProbe.AssertWithinBudget(snapshot);

            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.StartEnabled, "Start buttons should be enabled after a successful file open + validation.");
            Assert.NotEmpty(vm.ColumnsInGrid);
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    });

    [Fact]
    public Task Import_TypeOverrideThenRepopulation_KeepsStartButtonsEnabled() => RunOnUi(() =>
    {
        string csv = Path.Combine(Path.GetTempPath(), $"jbt_reopen_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csv, "id,price\n1,10.5\n2,20.75\n3,1\n");

            var appData = HeadlessProductHost.CreateAppData("IMPORT_START_CONNECTION");

            var file = new Mock<IStorageFile>();
            file.Setup(f => f.Path).Returns(new Uri(Path.GetFullPath(csv)));
            var storage = new Mock<IStorageProvider>();
            storage.Setup(s => s.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
                .ReturnsAsync((IReadOnlyList<IStorageFile>)[file.Object]);
            var helpers = new Mock<IAvaloniaSpecificHelpers>();
            helpers.Setup(h => h.GetStorageProvider()).Returns(storage.Object);

            var messageForUserTools = new Mock<IMessageForUserTools>();
            messageForUserTools
                .Setup(m => m.DispatcherActionInstance(It.IsAny<Action>()))
                .Callback<Action>(action => action());

            var resolver = new Mock<IDatabaseServiceResolver>();
            resolver
                .Setup(r => r.GetDatabaseService(
                    It.IsAny<IGeneralApplicationData>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<Action<string>?>()))
                .Returns(HeadlessProductHost.CreateLargeSchemaService(tablesPerSchema: 2, schemas: 2).Object);

            var vm = new ImportViewModel(
                helpers.Object,
                appData.Object,
                messageForUserTools.Object,
                Mock.Of<IDocumentCloseDecisionService>(),
                Mock.Of<IActiveDocumentManager>(),
                resolver.Object);

            var probe = new UiResponsivenessProbe(_output);
            var snapshot = probe.RunDuring(
                "Import.OverrideTypeThenRepopulate",
                async () =>
                {
                    vm.OpenFileForImportCommand.Execute(null);

                    var openDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                    while ((vm.ColumnsInGrid.Count == 0 || !vm.StartEnabled) && DateTime.UtcNow < openDeadline)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(20).ConfigureAwait(true);
                    }

                    Assert.True(vm.StartEnabled);
                    Assert.NotEmpty(vm.ColumnsInGrid);

                    // Override a column type (schedules its own re-validation), then re-populate
                    // the grid from the cached chooser, which now carries the override.
                    vm.ColumnsInGrid[1].SelectedChoice =
                        TypeChoice.All.First(c => c.Value == DbSimpleType.Nvarchar);
                    vm.SelIndexOpt = 1;

                    var revalidateDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                    while (!vm.StartEnabled && DateTime.UtcNow < revalidateDeadline)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(20).ConfigureAwait(true);
                    }
                },
                TimeSpan.FromSeconds(30));

            ResponsivenessMetricsWriter.Append(nameof(Import_TypeOverrideThenRepopulation_KeepsStartButtonsEnabled), snapshot);
            UiResponsivenessProbe.AssertWithinBudget(snapshot);

            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.StartEnabled, "Start buttons must stay enabled after a type override + grid repopulation.");
        }
        finally
        {
            try
            {
                File.Delete(csv);
            }
            catch (IOException)
            {
            }
        }
    });
}
