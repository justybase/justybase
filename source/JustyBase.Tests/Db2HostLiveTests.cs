using DB2Plugin;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services.Schema;

namespace JustyBase.Tests;

/// <summary>
/// Live Db2 LUW host-layer checks: schema tree groups, capability-gated context
/// menu and driver DDL. Soft-skips unless DB2_LIVE_TEST_* is configured; set
/// DB2_LIVE_TEST_REQUIRED=true to turn a missing server or privilege error into a failure.
/// </summary>
[Trait("Category", "LiveDatabase")]
public sealed class Db2HostLiveTests
{
    private static bool Required
        => string.Equals(
            Environment.GetEnvironmentVariable("DB2_LIVE_TEST_REQUIRED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    [Trait("Category", "Live")]
    public async Task Live_HostTreeMenuAndDdl_WhenDb2IsConfigured()
    {
        if (!TryCreateService(out var service, out string database, out string schema))
        {
            return;
        }

        if (!TryCacheDictionary(service))
        {
            return;
        }

        try
        {
            Assert.Contains(
                schema,
                service.GetSchemas(database, ""),
                StringComparer.OrdinalIgnoreCase);

            var tables = service.GetDbObjects(database, schema, "", TypeInDatabaseEnum.Table).ToArray();
            if (tables.Length == 0)
            {
                Console.WriteLine($"Db2 live host test skipped: schema {schema} has no tables.");
                return;
            }

            string[] menu = SchemaContextMenuCatalog
                .ForType(TypeInDatabaseEnum.Table, service.Capabilities)
                .Select(entry => entry.Title)
                .ToArray();

            Assert.Contains("Top 100", menu);
            Assert.Contains("Export DDL + data…", menu);
            Assert.Contains("Impact analysis", menu);
            Assert.DoesNotContain("Deleted rows", menu);
            Assert.DoesNotContain("Show distribution chart", menu);

            string ddl = await service.GetCreateTableText(database, schema, tables[0].Name);
            Assert.Contains("CREATE TABLE", ddl, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DISTRIBUTE ON", ddl, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (!Required && IsConnectionFailure(ex))
        {
            Console.WriteLine($"Db2 live host test soft-skipped (connection/catalog): {ex.Message.Trim()}");
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_TablespacesAreDatabaseLevelObjects()
    {
        if (!TryCreateService(out var service, out string database, out _))
        {
            return;
        }

        if (!TryCacheDictionary(service))
        {
            return;
        }

        try
        {
            var tablespaces = service.GetTablespaces(database).ToArray();

            Assert.NotEmpty(tablespaces);
            Assert.All(tablespaces, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        }
        catch (Exception ex) when (!Required && IsConnectionFailure(ex))
        {
            Console.WriteLine($"Db2 tablespace live test soft-skipped (connection/catalog): {ex.Message.Trim()}");
        }
    }

    private static bool TryCacheDictionary(DB2DatabaseService service)
    {
        try
        {
            service.CacheMainDictionary();
            return true;
        }
        catch (Exception ex) when (!Required && IsConnectionFailure(ex))
        {
            Console.WriteLine($"Db2 live host test soft-skipped (connection): {ex.Message.Trim()}");
            return false;
        }
    }

    private static bool IsConnectionFailure(Exception exception)
        => exception is System.Data.Common.DbException
            || exception.InnerException is System.Data.Common.DbException
            || exception.InnerException is System.Net.Sockets.SocketException
            || exception.Message.Contains("SQLCODE", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("SQL0", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateService(
        out DB2DatabaseService service,
        out string database,
        out string schema)
    {
        service = null!;
        database = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_DATABASE") ?? "";
        schema = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_CURRENT_SCHEMA")
            ?? Environment.GetEnvironmentVariable("DB2_LIVE_TEST_SCHEMA")
            ?? "";
        string host = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_HOST") ?? "";
        string user = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_USER") ?? "";
        string password = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_PASSWORD") ?? "";
        string port = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_PORT") ?? "50000";

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(password))
        {
            string message =
                "Db2 host live test not executed: set DB2_LIVE_TEST_HOST, DB2_LIVE_TEST_DATABASE, "
                + "DB2_LIVE_TEST_USER and DB2_LIVE_TEST_PASSWORD.";
            if (Required)
            {
                throw new InvalidOperationException(message);
            }

            Console.WriteLine(message);
            return false;
        }

        var options = new DbConnectionOptions(
            Username: user,
            Password: password,
            Port: port,
            Ip: host,
            Database: database,
            ConnectionTimeout: 30)
        {
            Db2Options = string.IsNullOrWhiteSpace(schema)
                ? null
                : new Db2ConnectionOptions { CurrentSchema = schema },
        };

        service = new DB2DatabaseService(options);
        if (string.IsNullOrWhiteSpace(schema))
        {
            schema = user.ToUpperInvariant();
        }

        return true;
    }
}
