using JustyBase.Common.Contracts;
using JustyBase.NetezzaCatalogSql;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services.Documents;

namespace JustyBase.Services;

public sealed class NetezzaSessionInfo
{
    public long SessionId { get; init; }
    public string UserName { get; init; } = "";
    public string Database { get; init; } = "";
    public string Status { get; init; } = "";
    public string Command { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string SqlPreview { get; init; } = "";
    public string KillSql { get; init; } = "";
    /// <summary>Engine-specific name for terminating this session.</summary>
    public string TerminationLabel { get; init; } = "Drop session";
}

public sealed class NetezzaSkewSlice
{
    public long DataSliceId { get; init; }
    public long RowCount { get; init; }
}

public sealed class NetezzaSkewResult
{
    public string QualifiedTable { get; init; } = "";
    public IReadOnlyList<NetezzaSkewSlice> Slices { get; init; } = [];
    public long MinRows { get; init; }
    public long MaxRows { get; init; }
    public long TotalRows { get; init; }
    public double SkewRatio { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>
/// Netezza session list / kill and table skew queries (Lite sessionMonitor + Legacy _V_SESSION patterns).
/// </summary>
public sealed class NetezzaSessionMonitorService
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _logger;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    public NetezzaSessionMonitorService(
        IGeneralApplicationData generalApplicationData,
        ISimpleLogger logger,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _logger = logger;
        _databaseServiceResolver = databaseServiceResolver;
    }

    public static string BuildSessionSnapshotSql() => NetezzaSystemSql.SessionMonitorSnapshotSql;

    /// <summary>
    /// Db2 LUW connections visible in the current database. The monitor's own
    /// short-lived connection is filtered out by application handle.
    /// </summary>
    public static string BuildDb2SessionSnapshotSql() => """
        SELECT C.APPLICATION_HANDLE
             , C.CLIENT_PID
             , TRIM(C.SESSION_AUTH_ID)
             , TRIM(C.APPLICATION_NAME)
             , VARCHAR(C.CONNECTION_START_TIME)
             , TRIM(C.WORKLOAD_OCCURRENCE_STATE)
             , TRIM(C.CLIENT_APPLNAME)
             , TRIM(C.CLIENT_IPADDR)
             , TRIM(C.CLIENT_USERID)
          FROM TABLE(SYSPROC.MON_GET_CONNECTION(CAST(NULL AS BIGINT), -2)) AS C
         WHERE C.APPLICATION_HANDLE <> SYSPROC.MON_GET_APPLICATION_HANDLE()
         ORDER BY C.CONNECTION_START_TIME DESC
         FETCH FIRST 1000 ROWS ONLY
        """;

    public static string BuildDb2CurrentApplicationHandleSql()
        => "VALUES SYSPROC.MON_GET_APPLICATION_HANDLE()";

    /// <summary>
    /// FORCE APPLICATION via ADMIN_CMD. This ends the whole connection, not a
    /// single statement; Db2 requires SYSADM, SYSCTRL or SYSMAINT authority.
    /// </summary>
    public static string BuildDb2ForceApplicationSql(long applicationHandle)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(applicationHandle);
        return $"CALL SYSPROC.ADMIN_CMD('force application ( {applicationHandle} ) MODE ASYNC')";
    }

    public static string BuildSkewSnapshotSql(string qualifiedTable) =>
        NetezzaSystemSql.GetTableSkewByDatasliceSql(qualifiedTable);

    public async Task<IReadOnlyList<NetezzaSessionInfo>> GetSessionsAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return [];
        }

        var db = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
        if (db is null)
        {
            return [];
        }

        return db.DatabaseType == DatabaseTypeEnum.DB2
            ? await GetDb2SessionsAsync(db, cancellationToken).ConfigureAwait(false)
            : await GetNetezzaSessionsAsync(db, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<NetezzaSessionInfo>> GetNetezzaSessionsAsync(
        IDatabaseService db,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var list = new List<NetezzaSessionInfo>();
            using var con = db.GetConnection(null);
            con.Open();
            using var cmd = db.CreateCommandFromConnection(con);
            cmd.CommandText = BuildSessionSnapshotSql();
            cmd.CommandTimeout = 60;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                long sessionId = Convert.ToInt64(rdr.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
                list.Add(new NetezzaSessionInfo
                {
                    SessionId = sessionId,
                    UserName = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Database = rdr.IsDBNull(2) ? "" : Convert.ToString(rdr.GetValue(2), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Status = rdr.IsDBNull(3) ? "" : Convert.ToString(rdr.GetValue(3), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Command = rdr.IsDBNull(4) ? "" : Convert.ToString(rdr.GetValue(4), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    IpAddress = rdr.IsDBNull(5) ? "" : Convert.ToString(rdr.GetValue(5), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    SqlPreview = rdr.IsDBNull(6) ? "" : Convert.ToString(rdr.GetValue(6), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    KillSql = $"{NetezzaSystemSql.GetDropSessionSql(sessionId)};",
                    TerminationLabel = "Drop session",
                });
            }

            return (IReadOnlyList<NetezzaSessionInfo>)list;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<NetezzaSessionInfo>> GetDb2SessionsAsync(
        IDatabaseService db,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var list = new List<NetezzaSessionInfo>();
            using var con = db.GetConnection(null);
            con.Open();
            using var cmd = db.CreateCommandFromConnection(con);
            cmd.CommandText = BuildDb2SessionSnapshotSql();
            cmd.CommandTimeout = 60;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                long applicationHandle = Convert.ToInt64(rdr.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
                string clientApplication = rdr.IsDBNull(6)
                    ? ""
                    : Convert.ToString(rdr.GetValue(6), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                list.Add(new NetezzaSessionInfo
                {
                    SessionId = applicationHandle,
                    UserName = rdr.IsDBNull(2) ? "" : Convert.ToString(rdr.GetValue(2), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Database = db.Database,
                    Status = rdr.IsDBNull(5) ? "" : Convert.ToString(rdr.GetValue(5), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Command = string.IsNullOrWhiteSpace(clientApplication)
                        ? (rdr.IsDBNull(3) ? "" : Convert.ToString(rdr.GetValue(3), System.Globalization.CultureInfo.InvariantCulture) ?? "")
                        : clientApplication,
                    IpAddress = rdr.IsDBNull(7) ? "" : Convert.ToString(rdr.GetValue(7), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    SqlPreview = rdr.IsDBNull(8) ? "" : Convert.ToString(rdr.GetValue(8), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    KillSql = BuildDb2ForceApplicationSql(applicationHandle),
                    TerminationLabel = "Force application",
                });
            }

            return (IReadOnlyList<NetezzaSessionInfo>)list;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task KillSessionAsync(string connectionName, long sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var db = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
        if (db is null)
        {
            throw new InvalidOperationException($"Connection '{connectionName}' is not available.");
        }

        if (db.DatabaseType == DatabaseTypeEnum.DB2)
        {
            await Task.Run(() => ForceDb2Application(db, sessionId), cancellationToken).ConfigureAwait(false);
            return;
        }

        await Task.Run(() =>
        {
            using var con = db.GetConnection(null);
            con.Open();
            using var cmd = db.CreateCommandFromConnection(con);
            cmd.CommandText = NetezzaSystemSql.GetDropSessionSql(sessionId);
            cmd.CommandTimeout = 30;
            cmd.ExecuteNonQuery();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ForceDb2Application(IDatabaseService db, long applicationHandle)
    {
        using var con = db.GetConnection(null);
        con.Open();

        using (var current = db.CreateCommandFromConnection(con))
        {
            current.CommandText = BuildDb2CurrentApplicationHandleSql();
            current.CommandTimeout = 30;
            object? result = current.ExecuteScalar();
            if (result is not null and not DBNull
                && Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture) == applicationHandle)
            {
                throw new InvalidOperationException("The session monitor cannot terminate its own Db2 connection.");
            }
        }

        using var cmd = db.CreateCommandFromConnection(con);
        cmd.CommandText = BuildDb2ForceApplicationSql(applicationHandle);
        cmd.CommandTimeout = 60;
        using var reader = cmd.ExecuteReader();
        do
        {
            while (reader.Read())
            {
            }
        }
        while (reader.NextResult());
    }

    public async Task<NetezzaSkewResult> GetTableSkewAsync(
        string connectionName,
        string database,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        var qualified = string.IsNullOrWhiteSpace(database)
            ? $"{schema}.{table}"
            : $"{database}.{schema}.{table}";

        return await Task.Run(() =>
        {
            var slices = new List<NetezzaSkewSlice>();
            var db = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
            if (db is null)
            {
                return new NetezzaSkewResult
                {
                    QualifiedTable = qualified,
                    Summary = "No database connection."
                };
            }

            using var con = db.GetConnection(database);
            con.Open();
            using var cmd = db.CreateCommandFromConnection(con);
            cmd.CommandText = BuildSkewSnapshotSql(qualified);
            cmd.CommandTimeout = 300;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                slices.Add(new NetezzaSkewSlice
                {
                    DataSliceId = Convert.ToInt64(rdr.GetValue(0), System.Globalization.CultureInfo.InvariantCulture),
                    RowCount = Convert.ToInt64(rdr.GetValue(1), System.Globalization.CultureInfo.InvariantCulture)
                });
            }

            if (slices.Count == 0)
            {
                return new NetezzaSkewResult
                {
                    QualifiedTable = qualified,
                    Slices = slices,
                    Summary = "No slice data returned."
                };
            }

            long min = slices.Min(s => s.RowCount);
            long max = slices.Max(s => s.RowCount);
            long total = slices.Sum(s => s.RowCount);
            double skew = max == 0 ? 0 : (double)(max - min) / max;

            return new NetezzaSkewResult
            {
                QualifiedTable = qualified,
                Slices = slices,
                MinRows = min,
                MaxRows = max,
                TotalRows = total,
                SkewRatio = skew,
                Summary = $"slices={slices.Count}, min={min}, max={max}, total={total}, skew={(skew * 100):0.##}%"
            };
        }, cancellationToken);
    }
}
