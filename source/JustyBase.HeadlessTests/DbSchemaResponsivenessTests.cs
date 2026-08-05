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
        Assert.DoesNotContain(node.Children, c => c.Name == "Loading...");
        Assert.Contains(node.Children, c => c.Name == "main");

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    });
}
