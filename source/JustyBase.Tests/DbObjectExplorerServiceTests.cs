using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Documents;
using Moq;

namespace JustyBase.Tests;

public sealed class DbObjectExplorerServiceTests
{
    private readonly Mock<IGeneralApplicationData> _appData = new();
    private readonly Mock<IDatabaseServiceResolver> _resolver = new();
    private readonly DbObjectExplorerService _sut;

    public DbObjectExplorerServiceTests()
    {
        _sut = new DbObjectExplorerService(_appData.Object, _resolver.Object);
    }

    [Fact]
    public void GetDropCode_ForwardsToDatabaseService()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetTableDropCode("t")).Returns("DROP TABLE t;");

        var result = _sut.GetDropCode(db.Object, "t");

        Assert.Equal("DROP TABLE t;", result);
        db.Verify(s => s.GetTableDropCode("t"), Times.Once);
    }

    [Fact]
    public void GetRenameCode_ForwardsToDatabaseService()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetTableRenameCode("t")).Returns("ALTER TABLE t RENAME TO t2;");

        Assert.Equal("ALTER TABLE t RENAME TO t2;", _sut.GetRenameCode(db.Object, "t"));
    }

    [Fact]
    public void GetCreateFromCode_ForwardsToDatabaseService()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetCreateFromCode("t")).Returns("CREATE TABLE t AS SELECT * FROM src;");

        Assert.Equal("CREATE TABLE t AS SELECT * FROM src;", _sut.GetCreateFromCode(db.Object, "t"));
    }

    [Fact]
    public void GetGroomCode_CallsGetGroomWithNullDatabaseAndSchema()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetGroom(null!, null!, "t")).Returns("GROOM TABLE t;");

        Assert.Equal("GROOM TABLE t;", _sut.GetGroomCode(db.Object, "t"));
        db.Verify(s => s.GetGroom(null!, null!, "t"), Times.Once);
    }

    [Fact]
    public void GetGenerateStatsCode_CallsGetGenerateStatsWithNullDatabaseAndSchema()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetGenerateStats(null!, null!, "t")).Returns("GENERATE STATISTICS ON t;");

        Assert.Equal("GENERATE STATISTICS ON t;", _sut.GetGenerateStatsCode(db.Object, "t"));
        db.Verify(s => s.GetGenerateStats(null!, null!, "t"), Times.Once);
    }

    [Fact]
    public void GetSelectCode_ForwardsToShortSelect()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetShortSelectCode("t")).Returns("SELECT * FROM t LIMIT 100;");

        Assert.Equal("SELECT * FROM t LIMIT 100;", _sut.GetSelectCode(db.Object, "t"));
    }

    [Fact]
    public async Task GetDdlCode_ForwardsToGetCreateTableText()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetCreateTableText("db", "sch", "t", null, null, null, null))
            .ReturnsAsync("CREATE TABLE t (id INT);");

        var result = await _sut.GetDdlCode(db.Object, "db", "sch", "t");

        Assert.Equal("CREATE TABLE t (id INT);", result);
    }

    [Fact]
    public async Task GetRecreateCode_ForwardsToGetReCreateTableText()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.GetReCreateTableText("db", "sch", "t"))
            .ReturnsAsync("DROP TABLE t; CREATE TABLE t (id INT);");

        var result = await _sut.GetRecreateCode(db.Object, "db", "sch", "t");

        Assert.Equal("DROP TABLE t; CREATE TABLE t (id INT);", result);
    }

    [Fact]
    public async Task EnsureDatabaseServiceAsync_SameConnectionName_ReturnsCurrent()
    {
        var current = new Mock<IDatabaseService>();
        current.SetupGet(s => s.Name).Returns("prod");

        var result = await _sut.EnsureDatabaseServiceAsync(current.Object, "prod");

        Assert.Same(current.Object, result);
    }

    [Fact]
    public void FindFromName_ThreePartName_ReturnsSingleMatch()
    {
        var dbObject = new DatabaseObject(1, "EMP", null, TypeInDatabaseEnum.Table, "TABLE", "admin", null);
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.FindDbObject("DB1", "PUBLIC", "EMP", true))
            .Returns([(dbObject, "PUBLIC")]);

        var (found, schema, database) = _sut.FindFromName(db.Object, "DB1.PUBLIC.EMP", cleanNames: true, selectedDatabase: null);

        Assert.Same(dbObject, found);
        Assert.Equal("PUBLIC", schema);
        Assert.Equal("DB1", database);
    }

    [Fact]
    public void FindFromName_TwoPartName_UsesSelectedDatabaseFallback()
    {
        var dbObject = new DatabaseObject(2, "T", null, TypeInDatabaseEnum.Table, "TABLE", "admin", null);
        var db = new Mock<IDatabaseService>();
        db.SetupGet(s => s.Database).Returns("FALLBACK");
        db.Setup(s => s.FindDbObject("SELECTED", "SCH", "T", false))
            .Returns([(dbObject, "SCH")]);

        var (found, schema, database) = _sut.FindFromName(db.Object, "SCH.T", cleanNames: false, selectedDatabase: "SELECTED");

        Assert.Same(dbObject, found);
        Assert.Equal("SCH", schema);
        Assert.Equal("SELECTED", database);
    }

    [Fact]
    public void FindFromName_ZeroMatches_ReturnsNulls()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.FindDbObject("DB", "S", "T", true))
            .Returns(Array.Empty<(DatabaseObject, string)>());

        var (found, schema, database) = _sut.FindFromName(db.Object, "DB.S.T", cleanNames: true, selectedDatabase: null);

        Assert.Null(found);
        Assert.Null(schema);
        Assert.Null(database);
    }

    [Fact]
    public void FindFromName_MultipleMatches_ReturnsNulls()
    {
        var a = new DatabaseObject(1, "T", null, TypeInDatabaseEnum.Table, "TABLE", "a", null);
        var b = new DatabaseObject(2, "T", null, TypeInDatabaseEnum.View, "VIEW", "b", null);
        var db = new Mock<IDatabaseService>();
        db.Setup(s => s.FindDbObject("DB", "S", "T", true))
            .Returns([(a, "S"), (b, "S")]);

        var (found, schema, database) = _sut.FindFromName(db.Object, "DB.S.T", cleanNames: true, selectedDatabase: null);

        Assert.Null(found);
        Assert.Null(schema);
        Assert.Null(database);
    }
}
