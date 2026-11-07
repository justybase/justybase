using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class SessionMonitorSqlTests
{
    [Fact]
    public void Db2SnapshotUsesMonitorFunctionsAndSkipsTheMonitorConnection()
    {
        string sql = NetezzaSessionMonitorService.BuildDb2SessionSnapshotSql();

        Assert.Contains("SYSPROC.MON_GET_CONNECTION", sql);
        Assert.Contains("SYSPROC.MON_GET_APPLICATION_HANDLE()", sql);
        Assert.Contains("FETCH FIRST 1000 ROWS ONLY", sql);
        Assert.Contains("CLIENT_IPADDR", sql);
        Assert.Contains("SESSION_AUTH_ID", sql);
    }

    [Fact]
    public void Db2ForceApplicationUsesAdminCmdAndModeAsync()
    {
        string sql = NetezzaSessionMonitorService.BuildDb2ForceApplicationSql(17);

        Assert.Equal("CALL SYSPROC.ADMIN_CMD('force application ( 17 ) MODE ASYNC')", sql);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NetezzaSessionMonitorService.BuildDb2ForceApplicationSql(0));
    }

    [Fact]
    public void CurrentHandleUsesTheMonitorFunction()
    {
        string sql = NetezzaSessionMonitorService.BuildDb2CurrentApplicationHandleSql();

        Assert.Equal("VALUES SYSPROC.MON_GET_APPLICATION_HANDLE()", sql);
    }
}
