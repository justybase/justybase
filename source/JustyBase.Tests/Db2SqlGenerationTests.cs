using DB2Plugin;
using JustyBase.PluginCommon.Models;

namespace JustyBase.Tests;

/// <summary>
/// F0/P0 guard: Db2 schema actions must emit Db2 LUW SQL, never Netezza syntax.
/// These are pure string generators; metadata-reading methods are covered by
/// live/headless tests where a Db2 server is available.
/// </summary>
public sealed class Db2SqlGenerationTests
{
    private static DB2DatabaseService CreateService()
        => new(new DbConnectionOptions(Username: "db2inst1", Database: "TESTDB"));

    [Fact]
    public void ShortSelect_UsesFetchFirstNotLimit()
    {
        string sql = CreateService().GetShortSelectCode("APP.ORDERS");

        Assert.Contains("FETCH FIRST 100 ROWS ONLY", sql);
        Assert.DoesNotContain("LIMIT", sql);
    }

    [Fact]
    public void Rename_UsesRenameTableNotAlterTable()
    {
        string sql = CreateService().GetTableRenameCode("APP.ORDERS");

        Assert.Equal("RENAME TABLE APP.ORDERS TO NEW_NAME;", sql);
    }

    [Fact]
    public void CreateFrom_UsesWithData()
    {
        string sql = CreateService().GetCreateFromCode("APP.ORDERS");

        Assert.Contains(") WITH DATA;", sql);
        Assert.DoesNotContain("DISTRIBUTE ON RANDOM", sql);
    }

    [Fact]
    public void Groom_UsesReorgTable()
    {
        string sql = CreateService().GetGroom("TESTDB", "APP", "ORDERS");

        Assert.Equal("REORG TABLE APP.ORDERS;", sql);
    }

    [Fact]
    public void Statistics_UsesRunstats()
    {
        string sql = CreateService().GetGenerateStats("TESTDB", "APP", "ORDERS");

        Assert.Equal("RUNSTATS ON TABLE APP.ORDERS WITH DISTRIBUTION AND DETAILED INDEXES ALL;", sql);
    }

    [Fact]
    public void SynonymTemplate_UsesCreateAlias()
    {
        string sql = CreateService().GetCreateSynonymPatternText();

        Assert.StartsWith("CREATE ALIAS", sql);
        Assert.DoesNotContain("CREATE SYNONYM", sql);
    }

    [Theory]
    [InlineData("MYPROC - 2", "CALL APP.MYPROC(<ARG1>, <ARG2>);")]
    [InlineData("MYPROC - 0", "CALL APP.MYPROC;")]
    [InlineData("MYPROC(<INTEGER>)", "CALL APP.MYPROC(<INTEGER>);")]
    public void ProcedureCall_ParsesTreeSignature(string treeName, string expected)
    {
        string sql = CreateService().GetCreateProcedureCall("TESTDB", "APP", treeName);

        Assert.Equal(expected, sql);
    }

    [Fact]
    public void Capabilities_AdvertiseOnlyImplementedDb2Actions()
    {
        DatabaseCapabilities capabilities = CreateService().Capabilities;

        Assert.True(capabilities.SupportsDdl);
        Assert.True(capabilities.SupportsTop100Select);
        Assert.True(capabilities.SupportsGroomTable);
        Assert.True(capabilities.SupportsTableStatistics);
        Assert.True(capabilities.SupportsRecreateTable);
        Assert.True(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsDeletedRows);
        Assert.False(capabilities.SupportsTableDistribution);
        Assert.False(capabilities.SupportsGrants);
        Assert.True(capabilities.SupportsExport);
        Assert.False(capabilities.SupportsExternalTables);
    }
}
