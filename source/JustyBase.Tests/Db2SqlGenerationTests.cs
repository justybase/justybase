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
        Assert.True(capabilities.SupportsComments);
        Assert.True(capabilities.SupportsExplain);
        Assert.True(capabilities.SupportsFindUsages);
        Assert.True(capabilities.SupportsTop100Select);
        Assert.True(capabilities.SupportsGroomTable);
        Assert.True(capabilities.SupportsTableStatistics);
        Assert.True(capabilities.SupportsRecreateTable);
        Assert.True(capabilities.SupportsIndexes);
        Assert.True(capabilities.SupportsPartitions);
        Assert.True(capabilities.SupportsTriggers);
        Assert.True(capabilities.SupportsTablespaces);
        Assert.True(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsDeletedRows);
        Assert.False(capabilities.SupportsTableDistribution);
        Assert.False(capabilities.SupportsGrants);
        Assert.True(capabilities.SupportsExport);
        Assert.False(capabilities.SupportsExternalTables);
    }

    [Fact]
    public void AddComment_UsesCommentOnTable()
    {
        string sql = CreateService().GetAddComment("ORDERS", "TESTDB", "APP");

        Assert.Equal("COMMENT ON TABLE APP.ORDERS IS '<comment>';", sql);
    }

    [Fact]
    public void Explain_UsesExplainPlanFor()
    {
        string sql = CreateService().GetExplainCode("TESTDB", "APP", "ORDERS");

        Assert.Equal("EXPLAIN PLAN FOR SELECT * FROM APP.ORDERS;", sql);
    }

    [Fact]
    public void DropIndexAndTrigger_DoNotUseIfExists()
    {
        var service = CreateService();

        Assert.Equal("DROP INDEX APP.IX_ORDERS;", service.GetDropIndex("TESTDB", "APP", "IX_ORDERS"));
        Assert.Equal("DROP TRIGGER APP.TRG_ORDERS;", service.GetDropTrigger("TESTDB", "APP", "TRG_ORDERS"));
    }

    [Fact]
    public void GetConnection_AppliesDb2Options()
    {
        var service = new DB2DatabaseService(new DbConnectionOptions(Username: "db2inst1", Database: "TESTDB")
        {
            Db2Options = new Db2ConnectionOptions
            {
                CurrentSchema = "APP",
                UseSsl = true,
                StatementTimeoutSeconds = 42,
            },
        });

        using var connection = service.GetConnection("TESTDB", pooling: false);

        Assert.Contains("CurrentSchema=APP", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StatementTimeout=42", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SSL", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }
}
