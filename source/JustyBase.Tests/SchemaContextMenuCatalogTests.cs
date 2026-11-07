using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services.Schema;

namespace JustyBase.Tests;

public class SchemaContextMenuCatalogTests
{
    [Fact]
    public void ForType_Table_IncludesCriticalNzActions()
    {
        var kinds = SchemaContextMenuCatalog.ForType(TypeInDatabaseEnum.Table)
            .Select(e => e.Kind)
            .ToHashSet();

        Assert.Contains(SchemaContextActionKind.ImportData, kinds);
        Assert.Contains(SchemaContextActionKind.Groom, kinds);
        Assert.Contains(SchemaContextActionKind.GenerateStatistics, kinds);
        Assert.Contains(SchemaContextActionKind.DistributionShow, kinds);
    }

    [Theory]
    [InlineData(SchemaContextActionKind.ImportData, TypeInDatabaseEnum.Table, "IMPORT_DATA")]
    [InlineData(SchemaContextActionKind.Groom, TypeInDatabaseEnum.Table, "GROOM")]
    [InlineData(SchemaContextActionKind.GenerateStatistics, TypeInDatabaseEnum.Table, "STATS")]
    [InlineData(SchemaContextActionKind.DistributionShow, TypeInDatabaseEnum.Table, "DISTRIBUTE_CHART_NZ")]
    [InlineData(SchemaContextActionKind.Top100, TypeInDatabaseEnum.View, "SELECT_VIEW")]
    [InlineData(SchemaContextActionKind.DdlToTab, TypeInDatabaseEnum.ExternalTable, "DDL_EXTERNAL")]
    [InlineData(SchemaContextActionKind.CountRows, TypeInDatabaseEnum.Table, "COUNT_ROWS")]
    public void GetCommandParameter_MapsExpected(SchemaContextActionKind kind, TypeInDatabaseEnum type, string expected)
    {
        Assert.Equal(expected, SchemaContextMenuCatalog.GetCommandParameter(kind, type));
    }

    [Fact]
    public void Entries_HaveUniqueSortOrders()
    {
        var orders = SchemaContextMenuCatalog.Entries.Select(e => e.SortOrder).ToList();
        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void ForType_WithDb2Capabilities_HidesNetezzaOnlyActions()
    {
        var kinds = SchemaContextMenuCatalog.ForType(TypeInDatabaseEnum.Table, DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.DB2))
            .Select(e => e.Kind)
            .ToHashSet();

        Assert.Contains(SchemaContextActionKind.Groom, kinds);
        Assert.Contains(SchemaContextActionKind.GenerateStatistics, kinds);
        Assert.Contains(SchemaContextActionKind.Recreate, kinds);
        Assert.DoesNotContain(SchemaContextActionKind.DistributionShow, kinds);
        Assert.DoesNotContain(SchemaContextActionKind.DistributionChange, kinds);
        Assert.DoesNotContain(SchemaContextActionKind.DeletedRows, kinds);
        Assert.DoesNotContain(SchemaContextActionKind.GrantTemplate, kinds);
    }

    [Fact]
    public void ForType_WithNetezzaCapabilities_KeepsNetezzaActions()
    {
        var kinds = SchemaContextMenuCatalog.ForType(TypeInDatabaseEnum.Table, DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.NetezzaSQL))
            .Select(e => e.Kind)
            .ToHashSet();

        Assert.Contains(SchemaContextActionKind.DistributionShow, kinds);
        Assert.Contains(SchemaContextActionKind.DeletedRows, kinds);
        Assert.Contains(SchemaContextActionKind.GrantTemplate, kinds);
        Assert.Contains(SchemaContextActionKind.Recreate, kinds);
    }

    [Theory]
    [InlineData("GROOM", "Reorganize table (REORG)…")]
    [InlineData("STATS", "Run statistics (RUNSTATS)…")]
    [InlineData("CREATE_SYNONYM", "Create alias template")]
    public void ResolveTitle_Db2_ReplacesNetezzaLabels(string parameter, string expected)
    {
        Assert.Equal(expected, SchemaContextMenuCatalog.ResolveTitle(parameter, "default", DatabaseTypeEnum.DB2));
    }

    [Fact]
    public void ResolveTitle_OtherEngines_KeepDefaultLabel()
    {
        Assert.Equal("GROOM…", SchemaContextMenuCatalog.ResolveTitle("GROOM", "GROOM…", DatabaseTypeEnum.NetezzaSQL));
    }
}
