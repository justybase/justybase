using JustyBase.PluginCommon.Enums;
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
}
