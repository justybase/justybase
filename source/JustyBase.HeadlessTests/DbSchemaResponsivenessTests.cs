using Avalonia.Threading;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using Xunit.Abstractions;

namespace JustyBase.HeadlessTests;

public sealed class DbSchemaResponsivenessTests : HeadlessSessionTestBase
{
    private readonly ITestOutputHelper _output;

    public DbSchemaResponsivenessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task DbSchema_ExpandableNode_ExposesPlaceholderBeforeAsyncChildrenArrive() => RunOnUi(() =>
    {
        const string connectionName = "SCHEMA_EXPAND_PLACEHOLDER";
        var appData = HeadlessProductHost.CreateAppData(connectionName);
        var fakeService = DelayedDatabaseServiceMock.Create(
            delayMs: DelayedDatabaseServiceMock.DefaultInjectedDelayMs,
            tablesPerSchema: 5,
            schemas: 2,
            delayGetDatabases: true,
            delayGetSchemas: false,
            delayGetDbObjects: false);
        // Already connected: the preload branch is skipped, which is the state a
        // normal desktop session is in when the user expands the tree root.
        fakeService.SetupProperty(s => s.ConnectedLevel, DatabaseConnectedLevel.ConnectedDatabaseObjects);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
        _ = DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: fakeService.Object);

        var node = new DbSchemaModel(TypeInDatabaseEnum.Connection, DatabaseTypeEnum.Sqlite, appData.Object)
        {
            Name = connectionName,
            Info = "connection",
            ConnectionName = connectionName
        };

        var placeholderChildren = node.Children;

        // The grid needs at least one row at expand time, otherwise the node
        // collapses before the async load completes.
        Assert.Single(placeholderChildren);
        Assert.Equal("Loading…", placeholderChildren[0].Name);
        Assert.True(node.IsLoading);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    });

    [Fact]
    public Task DbSchema_ConnectedNode_ReplacesPlaceholderAndRaisesChildrenLoaded() => RunOnUi(() =>
    {
        const string connectionName = "SCHEMA_EXPAND_CONNECTED";
        var appData = HeadlessProductHost.CreateAppData(connectionName);
        var fakeService = DelayedDatabaseServiceMock.Create(
            delayMs: 50,
            tablesPerSchema: 5,
            schemas: 2,
            delayGetDatabases: false,
            delayGetSchemas: false,
            delayGetDbObjects: false);
        fakeService.SetupProperty(s => s.ConnectedLevel, DatabaseConnectedLevel.ConnectedDatabaseObjects);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
        _ = DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: fakeService.Object);

        var node = new DbSchemaModel(TypeInDatabaseEnum.Connection, DatabaseTypeEnum.Sqlite, appData.Object)
        {
            Name = connectionName,
            Info = "connection",
            ConnectionName = connectionName
        };

        var raised = 0;
        node.ChildrenLoaded += (_, _) => raised++;

        _ = node.Children;
        Assert.Single(node.Children);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline
            && (!node.AreChildrenLoaded || node.Children.Any(c => c.Name == "Loading…")))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Dispatcher.UIThread.RunJobs();

        Assert.True(node.AreChildrenLoaded);
        Assert.Equal(1, raised);
        Assert.Contains(node.Children, c => c.Name == "main");
        Assert.DoesNotContain(node.Children, c => c.Name == "Loading…");

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    });

    [Fact]
    public Task DbSchema_ConnectionExpand_WithDelayedGetDatabases_StaysWithinStallBudget() => RunOnUi(() =>
    {
        const string connectionName = "SCHEMA_EXPAND_CONNECTION";
        const int injectedDelayMs = DelayedDatabaseServiceMock.DefaultInjectedDelayMs;

        var appData = HeadlessProductHost.CreateAppData(connectionName);
        var fakeService = DelayedDatabaseServiceMock.Create(
            delayMs: injectedDelayMs,
            tablesPerSchema: 10,
            schemas: 2,
            delayGetDatabases: true,
            delayGetSchemas: false,
            delayGetDbObjects: false);
        fakeService.SetupProperty(s => s.ConnectedLevel, DatabaseConnectedLevel.ConnectedDatabaseObjects);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
        _ = DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: fakeService.Object);

        var node = new DbSchemaModel(TypeInDatabaseEnum.Connection, DatabaseTypeEnum.Sqlite, appData.Object)
        {
            Name = connectionName,
            Info = "connection",
            ConnectionName = connectionName
        };

        var probe = new UiResponsivenessProbe(_output);
        var snapshot = probe.RunDuring(
            "DbSchema.LoadChildrenAsync.Connection",
            () => node.LoadChildrenAsync(),
            TimeSpan.FromSeconds(15),
            injectedDelayMs: injectedDelayMs);

        ResponsivenessMetricsWriter.Append(nameof(DbSchema_ConnectionExpand_WithDelayedGetDatabases_StaysWithinStallBudget), snapshot);
        UiResponsivenessProbe.AssertWithinBudget(snapshot);

        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(node.Children);
        Assert.DoesNotContain(node.Children, c => c.Name == "Loading…");
        Assert.Contains(node.Children, c => c.Name == "main");

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    });
}
