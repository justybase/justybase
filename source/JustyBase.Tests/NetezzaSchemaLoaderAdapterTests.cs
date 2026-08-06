using DatabaseService = JustyBase.PluginDatabaseBase.Database.DatabaseService;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Models;
using NetezzaBase;

namespace JustyBase.Tests;

/// <summary>
/// Behavioral tests for the Netezza plugin adapter that feeds host schema stores from the
/// shared <c>NetezzaSchemaLoader</c> (the production path replacing the local row parsers).
/// </summary>
public sealed class NetezzaSchemaLoaderAdapterTests
{
    private sealed class TestableNetezza : NetezzaCommonClass
    {
        public TestableNetezza()
            : base("user", "password", "5480", "host", "db", 10)
        {
        }

        public void Prime(string database)
            => _databaseSchemaTable[database] = new(StringComparer.OrdinalIgnoreCase);

        public void LoadObjectsPublic(string database, System.Data.Common.DbConnection con)
            => LoadDatabaseObject(database, con);

        public void LoadColumnsPublic(string database, System.Data.Common.DbConnection con)
            => LoadColumns(database, con);

        public Dictionary<string, DatabaseObject> Objects(string database, string schema)
            => _databaseSchemaTable[database][schema];

        public Dictionary<int, ColumnInterval> Intervals(string database)
            => DatabaseTableIdColumnIntervalSpan[database];

        public DatabaseColumn[] Columns(string database)
            => DatabaseColumnsList[database];
    }

    private static object?[] Obj(int id, string name, string? desc, string schema, string type, string? owner = "DBA", DateTime? created = null)
        => [id, name, desc, schema, type, owner, created];

    private static object?[] Col(int objId, string name, string? desc, string type, object notNull, string? defaultValue = null)
        => [objId, name, desc, type, notNull, defaultValue];

    [Fact]
    public void LoadObjects_PopulatesHostStoreFromSharedLoader()
    {
        using var connection = new FakeCatalogConnection(
            objectRows:
            [
                Obj(1, "CUSTOMERS", "main", "PUBLIC", "TABLE"),
                Obj(2, "ACTIVE_CUSTOMERS", null, "PUBLIC", "VIEW"),
                Obj(3, "FLUID_PROC", null, "PUBLIC", "FLUID"),
            ]);

        var netezza = new TestableNetezza();
        netezza.Prime("SALES");
        netezza.LoadObjectsPublic("SALES", connection);

        var objects = netezza.Objects("SALES", "PUBLIC");
        Assert.Equal(3, objects.Count);

        var table = objects["CUSTOMERS"];
        Assert.Equal(1, table.Id);
        Assert.Equal("main", table.Desc);
        Assert.Equal(TypeInDatabaseEnum.Table, table.TypeInDatabase);
        Assert.Equal("TABLE", table.TextType);
        Assert.Equal("DBA", table.Owner);

        Assert.Equal(TypeInDatabaseEnum.View, objects["ACTIVE_CUSTOMERS"].TypeInDatabase);
        Assert.Equal(TypeInDatabaseEnum.Fluid, objects["FLUID_PROC"].TypeInDatabase);
    }

    [Fact]
    public void LoadColumns_RebuildsIntervalsAndColumnListFromSnapshot()
    {
        using var connection = new FakeCatalogConnection(
            objectRows:
            [
                Obj(1, "T1", null, "PUBLIC", "TABLE"),
                Obj(2, "T2", null, "PUBLIC", "TABLE"),
                Obj(3, "V1", null, "PUBLIC", "VIEW"),
                Obj(4, "P1", null, "PUBLIC", "PROCEDURE"),
            ],
            columnRows:
            [
                Col(1, "A", null, "INTEGER", true),
                Col(1, "B", "b desc", "VARCHAR(5)", false, "''"),
                Col(2, "X", null, "DECIMAL(10,2)", false),
                Col(3, "Y", null, "TIMESTAMP", false),
            ]);

        var netezza = new TestableNetezza();
        netezza.Prime("SALES");
        netezza.LoadObjectsPublic("SALES", connection);
        netezza.LoadColumnsPublic("SALES", connection);

        var intervals = netezza.Intervals("SALES");
        Assert.Equal(3, intervals.Count);
        Assert.Equal(new ColumnInterval { FirstIndex = 0, LastIndex = 2 }, intervals[1]);
        Assert.Equal(new ColumnInterval { FirstIndex = 2, LastIndex = 3 }, intervals[2]);
        Assert.Equal(new ColumnInterval { FirstIndex = 3, LastIndex = 4 }, intervals[3]);

        var columns = netezza.Columns("SALES");
        Assert.Equal(4, columns.Length);
        Assert.Equal("A", columns[0].Name);
        Assert.Equal("INTEGER", columns[0].FullTypeName);
        Assert.True(columns[0].ColumnNotNull);
        Assert.Equal("b desc", columns[1].Desc);
        Assert.Equal("''", columns[1].COLDEFAULT);
        Assert.False(columns[1].ColumnNotNull);
        Assert.Equal("DECIMAL(10,2)", columns[2].FullTypeName);
        Assert.Equal("Y", columns[3].Name);
        Assert.False(columns[3].ColumnNotNull);
    }

    [Fact]
    public void LoadColumns_WithDeferredSnapshot_ProducesEmptyStores()
    {
        using var connection = new FakeCatalogConnection(
            objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")],
            columnRows: [Col(1, "A", null, "INTEGER", false)]);

        var netezza = new TestableNetezza();
        netezza.Prime("SALES");
        netezza.LoadObjectsPublic("SALES", connection);

        // Simulate a deferred (lazy) snapshot by clearing the cached snapshot before columns load.
        var field = typeof(TestableNetezza)
            .BaseType!
            .GetField("_loaderSnapshots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var snapshots = (Dictionary<string, JustyBase.Netezza.Models.NetezzaSchemaSnapshot>)field.GetValue(netezza)!;
        snapshots["SALES"] = new JustyBase.Netezza.Models.NetezzaSchemaSnapshot([]);

        netezza.LoadColumnsPublic("SALES", connection);

        Assert.Empty(netezza.Intervals("SALES"));
        Assert.Empty(netezza.Columns("SALES"));
    }
}
