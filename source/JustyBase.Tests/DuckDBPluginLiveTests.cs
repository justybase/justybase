using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using DuckDbService = DuckDBPlugin.DuckDB;

namespace JustyBase.Tests;

public sealed class DuckDBPluginLiveTests
{
    [Fact]
    public async Task DuckDBPlugin_OpensFixtureAndCachesTablesViewsAndColumns()
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-duckdb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "metadata-fixture.duckdb");

        try
        {
            string catalogName;
            var fixtureService = new DuckDbService(new DbConnectionOptions(Database: databasePath));
            await using (DbConnection connection = fixtureService.GetConnection(null, pooling: false))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, "CREATE SCHEMA analytics");
                await ExecuteAsync(connection,
                    "CREATE TABLE main.orders (order_id BIGINT NOT NULL, amount DECIMAL(12, 2) NOT NULL DEFAULT 0, note VARCHAR)");
                await ExecuteAsync(connection,
                    "CREATE TABLE analytics.orders (warehouse_id INTEGER NOT NULL, label VARCHAR)");
                await ExecuteAsync(connection,
                    "CREATE VIEW analytics.order_summary AS SELECT order_id, amount, note FROM main.orders");

                await using DbCommand databaseCommand = connection.CreateCommand();
                databaseCommand.CommandText = "SELECT current_database()";
                catalogName = (string?)await databaseCommand.ExecuteScalarAsync()
                    ?? throw new InvalidOperationException("DuckDB did not return its current catalog name.");
            }

            // Use a fresh service so the metadata cache must reopen the fixture file
            // using the catalog name returned by DuckDB.
            var service = new DuckDbService(new DbConnectionOptions(Database: databasePath));
            service.CacheMainDictionary();

            string database = Assert.Single(service.GetDatabases(string.Empty));
            Assert.Equal(catalogName, database, ignoreCase: true);

            string[] schemas = service.GetSchemas(database, string.Empty).ToArray();
            Assert.Contains(schemas, schema => schema.Equals("main", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(schemas, schema => schema.Equals("analytics", StringComparison.OrdinalIgnoreCase));

            DatabaseObject mainOrders = Assert.Single(
                service.GetDbObjects(database, "main", "orders", TypeInDatabaseEnum.Table));
            Assert.Equal("orders", mainOrders.Name, ignoreCase: true);

            DatabaseObject analyticsOrders = Assert.Single(
                service.GetDbObjects(database, "analytics", "orders", TypeInDatabaseEnum.Table));
            Assert.Equal("orders", analyticsOrders.Name, ignoreCase: true);

            DatabaseObject orderSummary = Assert.Single(
                service.GetDbObjects(database, "analytics", "order_summary", TypeInDatabaseEnum.View));
            Assert.Equal("order_summary", orderSummary.Name, ignoreCase: true);

            DatabaseColumn[] mainColumns = service.GetColumns(database, "main", "orders", string.Empty).ToArray();
            Assert.Equal(["order_id", "amount", "note"], mainColumns.Select(column => column.Name.ToLowerInvariant()).ToArray());
            DatabaseColumn orderId = Assert.Single(mainColumns, column => column.Name.Equals("order_id", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("BIGINT", orderId.FullTypeName, StringComparison.OrdinalIgnoreCase);
            Assert.True(orderId.ColumnNotNull);
            DatabaseColumn amount = Assert.Single(mainColumns, column => column.Name.Equals("amount", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("DECIMAL", amount.FullTypeName, StringComparison.OrdinalIgnoreCase);
            Assert.True(amount.ColumnNotNull);
            Assert.NotNull(amount.COLDEFAULT);
            DatabaseColumn note = Assert.Single(mainColumns, column => column.Name.Equals("note", StringComparison.OrdinalIgnoreCase));
            Assert.False(note.ColumnNotNull);

            DatabaseColumn[] analyticsColumns = service.GetColumns(database, "analytics", "orders", string.Empty).ToArray();
            Assert.Equal(["warehouse_id", "label"], analyticsColumns.Select(column => column.Name.ToLowerInvariant()).ToArray());

            DatabaseColumn[] viewColumns = service.GetColumns(database, "analytics", "order_summary", string.Empty).ToArray();
            Assert.Equal(["order_id", "amount", "note"], viewColumns.Select(column => column.Name.ToLowerInvariant()).ToArray());

            await service.CacheAllObjects([TypeInDatabaseEnum.View], database);

            // The database argument is DuckDB's catalog name, not the fixture file path.
            // This also exercises table DDL after both metadata caches have loaded.
            var ddl = new StringBuilder();
            await service.GetCreateTableTextStringBuilder(ddl, database, "analytics", "orders");
            Assert.Contains("warehouse_id", ddl.ToString(), StringComparison.OrdinalIgnoreCase);

            Assert.True(service.IsItemSourceContains(
                TypeInDatabaseEnum.View,
                database,
                "analytics",
                "order_summary",
                procedureId: -1,
                comp: StringComparison.OrdinalIgnoreCase,
                searchWord: "order_id",
                rx: new Regex("order_id", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));

        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

}
