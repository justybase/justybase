using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.ViewModels.Tools;
using Moq;
using Dock.Model.Core;
using Xunit.Abstractions;

namespace JustyBase.HeadlessTests;

public sealed class SchemaSearchResponsivenessTests : HeadlessSessionTestBase
{
    private readonly ITestOutputHelper _output;

    public SchemaSearchResponsivenessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task SchemaSearch_LargeRefresh_StaysWithinStallBudget() => RunOnUi(() =>
    {
        const string connectionName = "SAMPLE_CONNECTION";
        var appData = HeadlessProductHost.CreateAppData(connectionName);
        var fakeService = HeadlessProductHost.CreateLargeSchemaService(tablesPerSchema: 400, schemas: 4);
        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
        _ = DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: fakeService.Object);

        var logVm = new LogToolViewModel(Mock.Of<IFactory>(), Mock.Of<IClipboardService>(), Mock.Of<IMessageForUserTools>());
        var vm = new SchemaSearchViewModel(Mock.Of<IFactory>(), appData.Object, Mock.Of<IMessageForUserTools>(), logVm)
        {
            ConnectionName = connectionName
        };

        // Measure VM refresh without hosting SchemaSearchView (requires App.axaml converters).
        var probe = new UiResponsivenessProbe(_output);
        var snapshot = probe.RunDuring(
            "SchemaSearch.RefreshDb.Large",
            () => vm.RefreshDbCmd.ExecuteAsync(null),
            TimeSpan.FromSeconds(30));

        ResponsivenessMetricsWriter.Append(nameof(SchemaSearch_LargeRefresh_StaysWithinStallBudget), snapshot);
        UiResponsivenessProbe.AssertWithinBudget(snapshot);

        Assert.True(vm.SchemaSearchItemCollections.Count > 0);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    });

    [Fact]
    public Task SchemaSearch_Refresh_WithDelayedGetDatabases_StaysWithinStallBudget() => RunOnUi(() =>
    {
        const string connectionName = "SAMPLE_CONNECTION_DELAYED";
        const int injectedDelayMs = DelayedDatabaseServiceMock.DefaultInjectedDelayMs;
        var appData = HeadlessProductHost.CreateAppData(connectionName);
        var fakeService = DelayedDatabaseServiceMock.Create(
            delayMs: injectedDelayMs,
            tablesPerSchema: 50,
            schemas: 2,
            delayGetDatabases: true,
            delayGetSchemas: false,
            delayGetDbObjects: false);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
        _ = DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: fakeService.Object);

        var logVm = new LogToolViewModel(Mock.Of<IFactory>(), Mock.Of<IClipboardService>(), Mock.Of<IMessageForUserTools>());
        var vm = new SchemaSearchViewModel(Mock.Of<IFactory>(), appData.Object, Mock.Of<IMessageForUserTools>(), logVm)
        {
            ConnectionName = connectionName
        };

        var probe = new UiResponsivenessProbe(_output);
        var snapshot = probe.RunDuring(
            "SchemaSearch.RefreshDb.DelayedGetDatabases",
            () => vm.RefreshDbCmd.ExecuteAsync(null),
            TimeSpan.FromSeconds(30),
            injectedDelayMs: injectedDelayMs);

        ResponsivenessMetricsWriter.Append(nameof(SchemaSearch_Refresh_WithDelayedGetDatabases_StaysWithinStallBudget), snapshot);
        UiResponsivenessProbe.AssertWithinBudget(snapshot);
        Assert.True(vm.SchemaSearchItemCollections.Count > 0);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    });

    [Fact]
    public async Task SchemaSearch_RapidRevealRequests_KeepOnlyLatestSelection()
    {
        Assert.NotNull(Session);

        await Session!.Dispatch(async () =>
        {
            const string connectionName = "SCHEMA_SEARCH_REVEAL_CONNECTION";
            var appData = HeadlessProductHost.CreateAppData(connectionName);
            var fakeService = DelayedDatabaseServiceMock.Create(
                delayMs: 300,
                tablesPerSchema: 10,
                schemas: 2,
                delayGetDatabases: true,
                delayGetSchemas: false,
                delayGetDbObjects: false);

            DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
            _ = DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: fakeService.Object);

            var factory = new Mock<IFactory>();
            var dbSchemaViewModel = new DbSchemaViewModel(
                factory.Object,
                Mock.Of<IClipboardService>(),
                appData.Object,
                Mock.Of<ISimpleLogger>(),
                Mock.Of<IMessageForUserTools>());
            var focusCount = 0;
            dbSchemaViewModel.FocusAndBringSelectionIntoView = () => focusCount++;
            factory
                .Setup(f => f.Find(It.IsAny<Func<IDockable, bool>>()))
                .Returns([dbSchemaViewModel]);

            var logViewModel = new LogToolViewModel(
                factory.Object,
                Mock.Of<IClipboardService>(),
                Mock.Of<IMessageForUserTools>());
            var schemaSearchViewModel = new SchemaSearchViewModel(
                factory.Object,
                appData.Object,
                Mock.Of<IMessageForUserTools>(),
                logViewModel)
            {
                ConnectionName = connectionName
            };

            var first = new SchemaSearchItem
            {
                Type = "Table",
                Name = "T1",
                Db = "main",
                Schema = "S1"
            };
            var latest = new SchemaSearchItem
            {
                Type = "Table",
                Name = "T2",
                Db = "main",
                Schema = "S1"
            };

            var firstReveal = schemaSearchViewModel.DoubleTappedAction(first);
            await Task.Delay(30).ConfigureAwait(true);
            var latestReveal = schemaSearchViewModel.DoubleTappedAction(latest);
            var allReveals = Task.WhenAll(firstReveal, latestReveal);

            var completed = await Task.WhenAny(
                allReveals,
                Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(true);

            Assert.Same(allReveals, completed);
            Assert.Equal("T2", dbSchemaViewModel.SelectedSchemaItem?.Name);
            Assert.Equal(1, focusCount);

            DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
        }, CancellationToken.None);
    }
}

public sealed class DualSqlConcurrencyTests : HeadlessSessionTestBase
{
    [Fact]
    public Task DualSqlExecution_Mocked_CompletesWithoutHang() => RunOnUi(() =>
    {
        var gate = new ManualResetEventSlim(false);
        var started = 0;
        var finished = 0;

        Task RunOne()
        {
            return Task.Run(() =>
            {
                Interlocked.Increment(ref started);
                if (Volatile.Read(ref started) >= 2)
                {
                    gate.Set();
                }

                Thread.Sleep(150);
                Interlocked.Increment(ref finished);
            });
        }

        var t1 = RunOne();
        var t2 = RunOne();

        Assert.True(gate.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, started);

        var completed = Task.WaitAll([t1, t2], TimeSpan.FromSeconds(10));
        Assert.True(completed);
        Assert.Equal(2, finished);
    });
}
