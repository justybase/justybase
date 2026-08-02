using JustyBase.Helpers.Shared;
using JustyBase.NetezzaDdl;

namespace JustyBase.Tests;

public sealed class NetezzaMaintenanceSqlTests
{
    [Fact]
    public void BuildGenerateStats_Express_UsesExpressKeyword()
    {
        var sql = NetezzaMaintenanceSql.BuildGenerateStats("DB.SCH.TBL", express: true);
        Assert.Equal("GENERATE EXPRESS STATISTICS ON DB.SCH.TBL;", sql);
    }

    [Fact]
    public void BuildGenerateStats_FullWithColumns_IncludesColumnList()
    {
        var sql = NetezzaMaintenanceSql.BuildGenerateStats("SCH.TBL", express: false, "COL1, COL2");
        Assert.Equal("GENERATE STATISTICS ON SCH.TBL (COL1, COL2);", sql);
    }

    [Fact]
    public void BuildGroom_QuotesCustomBackupset()
    {
        var sql = NetezzaMaintenanceSql.BuildGroom("T1", "VERSIONS", "42");
        Assert.Equal("GROOM TABLE T1 VERSIONS RECLAIM BACKUPSET '42';", sql);
    }

    [Fact]
    public void BuildGroom_DefaultBackupset_Unquoted()
    {
        var sql = NetezzaMaintenanceSql.BuildGroom("T1", "RECORDS ALL", "DEFAULT");
        Assert.Equal("GROOM TABLE T1 RECORDS ALL RECLAIM BACKUPSET DEFAULT;", sql);
    }

    [Fact]
    public void GenerateStatistics_OptionConstant_IsDistinctFromGroom()
    {
        Assert.Equal("Generate statistics", SqlDocumentViewModelHelper.CurrentOptionsListSTATS);
        Assert.NotEqual(SqlDocumentViewModelHelper.CurrentOptionsListGROOM, SqlDocumentViewModelHelper.CurrentOptionsListSTATS);

        var groom = NetezzaMaintenanceSql.BuildGroom("X", "RECORDS ALL", "DEFAULT");
        var stats = NetezzaMaintenanceSql.BuildGenerateStats("X", express: true);
        Assert.DoesNotContain("GROOM", stats, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GENERATE", groom, StringComparison.OrdinalIgnoreCase);
    }
}
