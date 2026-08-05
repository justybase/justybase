using JustyBase.Common.Contracts;
using JustyBase.NetezzaCatalogSql;
using JustyBase.PluginCommon.Contracts;
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
    public string KillSql => $"{NetezzaSystemSql.GetDropSessionSql(SessionId)};";
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

    public string BuildSessionSnapshotSql() => NetezzaSystemSql.SessionMonitorSnapshotSql;

    public string BuildSkewSnapshotSql(string qualifiedTable) =>
        NetezzaSystemSql.GetTableSkewByDatasliceSql(qualifiedTable);

    public async Task<IReadOnlyList<NetezzaSessionInfo>> GetSessionsAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<NetezzaSessionInfo>();
            var db = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
            if (db is null)
            {
                return (IReadOnlyList<NetezzaSessionInfo>)list;
            }

            using var con = db.GetConnection(null);
            con.Open();
            using var cmd = db.CreateCommandFromConnection(con);
            cmd.CommandText = BuildSessionSnapshotSql();
            cmd.CommandTimeout = 60;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                list.Add(new NetezzaSessionInfo
                {
                    SessionId = Convert.ToInt64(rdr.GetValue(0), System.Globalization.CultureInfo.InvariantCulture),
                    UserName = rdr.IsDBNull(1) ? "" : Convert.ToString(rdr.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Database = rdr.IsDBNull(2) ? "" : Convert.ToString(rdr.GetValue(2), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Status = rdr.IsDBNull(3) ? "" : Convert.ToString(rdr.GetValue(3), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Command = rdr.IsDBNull(4) ? "" : Convert.ToString(rdr.GetValue(4), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    IpAddress = rdr.IsDBNull(5) ? "" : Convert.ToString(rdr.GetValue(5), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    SqlPreview = rdr.IsDBNull(6) ? "" : Convert.ToString(rdr.GetValue(6), System.Globalization.CultureInfo.InvariantCulture) ?? ""
                });
            }

            return list;
        }, cancellationToken);
    }

    public async Task KillSessionAsync(string connectionName, long sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        }

        await Task.Run(() =>
        {
            var db = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
            if (db is null)
            {
                throw new InvalidOperationException($"Connection '{connectionName}' is not available.");
            }

            using var con = db.GetConnection(null);
            con.Open();
            using var cmd = db.CreateCommandFromConnection(con);
            cmd.CommandText = NetezzaSystemSql.GetDropSessionSql(sessionId);
            cmd.CommandTimeout = 30;
            cmd.ExecuteNonQuery();
        }, cancellationToken);
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
