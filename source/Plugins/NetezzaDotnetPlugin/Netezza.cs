using JustyBase.PluginDatabaseBase.Database;
using System.Data.Common;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Netezza;
using JustyBase.NetezzaCatalogSql;
using NetezzaBase;
using JustyBase.NetezzaDriver;

namespace NetezzaDotnetPlugin;

public sealed class Netezza : NetezzaCommonClass, INetezza, INetezzaDotnet
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.NetezzaSQL;
    public Netezza(string username, string password, string port, string ip, string db, int connectionTimeout) : base(username, password, port, ip, db, connectionTimeout)
    {
        DatabaseType = WHO_I_AM_CONST;
    }

    protected override string DriverName => "dotnet";

    private readonly Lock _lock = new();
    public override DbConnection GetConnection(string? databaseName, bool pooling = true)
    {
        NzConnection? conn;
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                databaseName = Database;
            }

            conn = new NzConnection(Username, Password, Ip, databaseName, int.Parse(Port));
            conn.NoticeReceived += Conn_NoticeReceived;
        }

        return conn;
    }

    private void Conn_NoticeReceived(object sender, NzNoticeEventArgs e)
    {
        DbMessageAction?.Invoke(e.Message);
    }

    public override (int position, int length) HandleExceptions(ReadOnlySpan<char> sql, Exception exception)
    {
        string? msg = exception.Message;
        if (string.IsNullOrEmpty(msg))
            return (-1, -1);

        return NetezzaErrorLocator.LocateInSql(msg, sql);
    }

    public async Task DropConnectionEmergencyModeAsync(DbConnection dbConnection)
    {
        if (dbConnection is NzConnection nzCon)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var conX = GetConnection(null, pooling: false);
                    int id = (int)nzCon.Pid!;
                    conX.Open();
                    var cmd = conX.CreateCommand();
                    cmd.CommandText = NetezzaSystemSql.GetSessionIdByPidSql(id);
                    var res = cmd.ExecuteScalar();
                    if (res is int intRes)
                    {
                        cmd.CommandText = NetezzaSystemSql.GetDropSessionSql(intRes);
                        cmd.ExecuteNonQuery();
                    }
                    conX.Close();
                }
                catch (Exception ex)
                {
                    Logger?.TrackError(ex, isCrash: false);
                }
            });
        }
    }

    public override IDatabaseRowReader GetDatabaseRowReader(DbDataReader reader)
    {
        if (reader is NzDataReader)
        {
            return new DatabaseRowReaderNetezzaDotnet(reader);
        }
        return new DatabaseRowReaderGeneral(reader);
    }
}
