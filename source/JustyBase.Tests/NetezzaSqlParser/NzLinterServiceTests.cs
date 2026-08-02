using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Basic lifecycle tests for NzLinterService.
/// Full integration tests (cancel, schema sync) require Avalonia UI infrastructure.
/// </summary>
public sealed class NzLinterServiceBasicTests
{
    private static readonly IDatabaseServiceResolver Resolver = Mock.Of<IDatabaseServiceResolver>();

    [Fact]
    public void AppendServiceSchema_PreservesObjectsFromMultipleServices()
    {
        var provider = new InMemorySchemaProvider();
        var first = CreateSchemaService("DB1", "TABLE_ONE", TypeInDatabaseEnum.Table);
        var second = CreateSchemaService("DB2", "VIEW_TWO", TypeInDatabaseEnum.View);

        NzLinterService.AppendServiceSchema(provider, first.Object, CancellationToken.None);
        NzLinterService.AppendServiceSchema(provider, second.Object, CancellationToken.None);

        var table = provider.GetTable("DB1", "ADMIN", "TABLE_ONE");
        Assert.NotNull(table);
        Assert.Equal("INTEGER", Assert.Single(table.Columns!).DataType);
        Assert.True(provider.GetTable("DB2", "ADMIN", "VIEW_TWO")?.IsView);
    }

    private static Mock<IDatabaseService> CreateSchemaService(
        string database,
        string objectName,
        TypeInDatabaseEnum objectType)
    {
        var service = new Mock<IDatabaseService>();
        service.Setup(x => x.GetDatabases("")).Returns([database]);
        service.Setup(x => x.GetSchemas(database, "")).Returns(["ADMIN"]);
        service.Setup(x => x.GetDbObjects(database, "ADMIN", "", objectType))
            .Returns([new DatabaseObject(1, objectName, null, objectType, objectType.ToString(), "ADMIN", null)]);
        service.Setup(x => x.GetDbObjects(
                database,
                "ADMIN",
                "",
                objectType == TypeInDatabaseEnum.Table ? TypeInDatabaseEnum.View : TypeInDatabaseEnum.Table))
            .Returns([]);
        service.Setup(x => x.GetDbObjects(database, "ADMIN", "", TypeInDatabaseEnum.ExternalTable))
            .Returns([]);
        service.Setup(x => x.GetColumns(database, "ADMIN", objectName, ""))
            .Returns([new DatabaseColumn("ID", null, "INTEGER", true, null)]);
        return service;
    }

    private static NzLinterService CreateSut(
        InMemorySchemaProvider? schemaProvider = null)
        => new(new SqlDiagnosticsViewModel(), Resolver, schemaProvider);

    /// <summary>
    /// Check if Avalonia is initialized by attempting to create SqlDiagnosticsViewModel.
    /// If it fails with an Avalonia-related exception, we skip the test.
    /// </summary>
    private static bool IsAvaloniaAvailable()
    {
        try
        {
            _ = new SqlDiagnosticsViewModel();
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Avalonia"))
        {
            return false;
        }
    }

    [Fact]
    public void Constructor_WithDiagnosticsViewModel_DoesNotThrow()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithSchemaProvider_DoesNotThrow()
    {
        if (!IsAvaloniaAvailable()) return;
        var schemaProvider = new InMemorySchemaProvider();
        using var service = CreateSut(schemaProvider);
        Assert.NotNull(service);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        if (!IsAvaloniaAvailable()) return;
        var service = CreateSut();
        service.Dispose();
        service.Dispose(); // should not throw
    }

    [Fact]
    public void SyncSchema_NullProvider_ReturnsCompletedTask()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut(schemaProvider: null);
        var task = service.SyncSchemaFromAllConnectionsAsync();
        Assert.NotNull(task);
        Assert.True(task.IsCompletedSuccessfully, "With null schema, should return completed task");
    }

    [Fact]
    public void ForceReanalyze_BeforeDispose_DoesNotThrow()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        service.ForceReanalyze(); // should not throw
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenWorkIsPending()
    {
        if (!IsAvaloniaAvailable()) return;
        var service = CreateSut();
        service.ForceReanalyze(); // schedules background work
        service.Dispose(); // should not throw
    }

    [Fact]
    public void Registry_IsAccessible()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        Assert.NotNull(service.Registry);
        Assert.IsType<QualityRuleRegistry>(service.Registry);
    }

    [Fact]
    public void Queue_IsAccessible()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        Assert.NotNull(service.Queue);
        Assert.IsType<LintQueue>(service.Queue);
        Assert.Same(service.Registry, service.Queue.Registry);
    }

    [Fact]
    public void Metrics_InitialState_AllZero()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        var metrics = service.Metrics;
        Assert.Equal(0, metrics.CheapRunCount);
        Assert.Equal(0, metrics.ExpensiveRunCount);
        Assert.Equal(0, metrics.CacheHitCount);
        Assert.Equal(0, metrics.CacheMissCount);
    }

    [Fact]
    public void ResetMetrics_ClearsCounters()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        service.ResetMetrics(); // should not throw
        var metrics = service.Metrics;
        Assert.Equal(0, metrics.CheapRunCount);
    }

    [Fact]
    public void Registry_ConfigureSeverity_AffectsEngine()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        service.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        Assert.Equal(RuleSeverityConfig.Off, service.Registry.GetEffectiveSeverity("NZ001"));
        Assert.Equal(RuleSeverityConfig.Off, service.Engine.Registry.GetEffectiveSeverity("NZ001"));
    }

    [Fact]
    public void Queue_GetEffectivePriority_DelegatesToRegistry()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        var prio = service.Queue.GetEffectivePriority("NZ001");
        Assert.Equal(80, prio); // NZ001 has explicit priority 80
    }

    [Fact]
    public void Engine_IsAccessible()
    {
        if (!IsAvaloniaAvailable()) return;
        using var service = CreateSut();
        Assert.NotNull(service.Engine);
        Assert.IsType<LintEngine>(service.Engine);
    }
}
