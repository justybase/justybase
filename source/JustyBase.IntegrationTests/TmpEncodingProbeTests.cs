using JustyBase.NetezzaDriver;

namespace JustyBase.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class TmpEncodingProbeTests
{
    [Fact]
    public void Probe_ServerEncoding()
    {
        using NzConnection conn = NetezzaLiveTestHost.OpenConnection();
        var sb = new System.Text.StringBuilder();
        foreach (string q in new[]
        {
            "SELECT DATABASE, ENCODING FROM _V_DATABASE",
            "SELECT * FROM _V_TABLE_STORAGE_STAT LIMIT 1"
        })
        {
            try
            {
                var r = NetezzaLiveTestHost.ExecuteReaderRows(conn, q, 40);
                sb.AppendLine($"--- {q}");
                foreach (var row in r)
                {
                    sb.AppendLine($"   {string.Join(" | ", row)}");
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"--- {q} FAILED: {ex.Message}");
            }
        }

        string tmpTable = "JB_TMPENC_" + System.Guid.NewGuid().ToString("N")[..8];
        NetezzaLiveTestHost.Execute(conn, $"CREATE TABLE {tmpTable} (TXT NVARCHAR(100)) DISTRIBUTE ON RANDOM;");
        NetezzaLiveTestHost.Execute(conn, $"INSERT INTO {tmpTable} VALUES ('żółć 中文 ✓');");
        var back = NetezzaLiveTestHost.ExecuteReaderRows(conn, $"SELECT TXT FROM {tmpTable}", 1);
        sb.AppendLine("--- direct insert round-trip");
        foreach (var row in back)
        {
            sb.AppendLine($"   [{row[0]}]");
        }
        NetezzaLiveTestHost.TryDrop(conn, tmpTable);

        var lit = NetezzaLiveTestHost.ExecuteReaderRows(conn, "SELECT 'żółć 中文 ✓' AS X FROM _V_DATABASE LIMIT 1", 1);
        sb.AppendLine("--- client literal (no storage)");
        foreach (var row in lit)
        {
            sb.AppendLine($"   [{row[0]}]");
        }

        try
        {
            NetezzaLiveTestHost.Execute(conn, "SET CLIENT_ENCODING TO 'UTF8'");
            var lit2 = NetezzaLiveTestHost.ExecuteReaderRows(conn, "SELECT 'żółć 中文 ✓' AS X FROM _V_DATABASE LIMIT 1", 1);
            sb.AppendLine("--- after SET CLIENT_ENCODING 'UTF8'");
            foreach (var row in lit2)
            {
                sb.AppendLine($"   [{row[0]}]");
            }
        }
        catch (System.Exception ex)
        {
            sb.AppendLine($"--- SET CLIENT_ENCODING failed: {ex.Message}");
        }

        foreach (string q in new[]
        {
            "SELECT CURRENT_DATABASE()",
            "SELECT * FROM _V_SESSION ORDER BY ATTNUM LIMIT 100",
            "SELECT SETTING, VALUE FROM _V_SETTINGS WHERE SETTING LIKE '%encod%'"
        })
        {
            try
            {
                var r = NetezzaLiveTestHost.ExecuteReaderRows(conn, q, 20);
                sb.AppendLine($"--- {q}");
                foreach (var row in r)
                {
                    sb.AppendLine($"   {string.Join(" | ", row)}");
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"--- {q} FAILED: {ex.Message}");
            }
        }

        foreach (string cmd in new[] { "set nz_encoding to 'utf8'", "set nz_encoding to 'utf-8'", "set nz_encoding to 'latin2'", "set nz_encoding to 'utf8'; set nz_encoding" })
        {
            try
            {
                NetezzaLiveTestHost.Execute(conn, cmd);
                var probe = NetezzaLiveTestHost.ExecuteReaderRows(conn, "SELECT 'żółć 中文 ✓' AS X FROM _V_DATABASE LIMIT 1", 1);
                sb.AppendLine($"--- after [{cmd}] => [{probe[0][0]}]");
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"--- [{cmd}] FAILED: {ex.Message}");
            }
        }

        var fromCol = NetezzaLiveTestHost.ExecuteReaderRows(conn, "SELECT DATABASE FROM _V_DATABASE LIMIT 1", 1);
        sb.AppendLine("--- ASCII catalog readback: " + fromCol[0][0]);

        string probePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jb-nz-probe.txt");
        System.IO.File.WriteAllText(probePath, sb.ToString());
    }
}
