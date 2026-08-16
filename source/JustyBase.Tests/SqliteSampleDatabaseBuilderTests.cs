using JustyBase.PluginCommon.Enums;
using JustyBase.SqliteDriver;
using JustyBase.SqliteDriver.Samples;
using Microsoft.Data.Sqlite;

namespace JustyBase.Tests;

public sealed class SqliteSampleDatabaseBuilderTests
{
    [Fact]
    public async Task SalesSampleCreatesLiveSchemaDataIndexesViewsAndTrigger()
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-sample-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string databaseFile = Path.Combine(root, "sales.sqlite");
        SqliteSamplePack sample = Assert.Single(SqliteSampleCatalog.Packs, item => item.Id == "sales");

        try
        {
            await SqliteSampleDatabaseBuilder.CreateAsync(
                root,
                Path.GetFileName(databaseFile),
                sample,
                sample.Objects.Select(item => item.Id));

            using (var connection = new SqliteConnection($"Data Source={databaseFile};Pooling=False"))
            {
                connection.Open();

            var schemaObjects = ReadObjectNames(connection);
            Assert.Contains("customers", schemaObjects.Tables);
            Assert.Contains("products", schemaObjects.Tables);
            Assert.Contains("orders", schemaObjects.Tables);
            Assert.Contains("order_items", schemaObjects.Tables);
            Assert.Contains("customer_totals", schemaObjects.Views);
            Assert.Contains("function_examples", schemaObjects.Views);
            Assert.Contains("trg_order_status_history", schemaObjects.Triggers);

            using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM customers;";
                Assert.Equal(3L, (long)count.ExecuteScalar()!);
            }

            using (var index = connection.CreateCommand())
            {
                index.CommandText = "SELECT 1 FROM pragma_index_list('orders') WHERE name = 'ix_orders_customer_date';";
                Assert.Equal(1L, (long)index.ExecuteScalar()!);
            }

            using (var functionExample = connection.CreateCommand())
            {
                functionExample.CommandText = "SELECT normalized_name, rounded_total FROM function_examples WHERE customer_id = 1;";
                using var reader = functionExample.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("ada lovelace", reader.GetString(0));
                Assert.Equal(243.9, reader.GetDouble(1), precision: 5);
            }

            using (var trigger = connection.CreateCommand())
            {
                trigger.CommandText = "UPDATE orders SET status = 'shipped' WHERE order_id = 1003;";
                trigger.ExecuteNonQuery();
            }

            using (var triggerAudit = connection.CreateCommand())
            {
                triggerAudit.CommandText = "SELECT COUNT(*) FROM order_status_history WHERE order_id = 1003;";
                Assert.Equal(1L, (long)triggerAudit.ExecuteScalar()!);
            }

            var service = new Sqlite(string.Empty, string.Empty, string.Empty, root, Path.GetFileName(databaseFile), 10);
            using (var driverConnection = service.GetConnection(null, pooling: false))
            {
                driverConnection.Open();
                service.CacheMainDictionary();
            }

                Assert.Contains(
                    service.GetDbObjects(Path.GetFileName(databaseFile), "main", "ix_orders", TypeInDatabaseEnum.Index),
                    item => item.Name == "ix_orders_customer_date");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SelectingFunctionExamplesAddsRequiredDependenciesAndMemoryIsRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-sample-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        SqliteSamplePack sample = Assert.Single(SqliteSampleCatalog.Packs, item => item.Id == "library");

        try
        {
            await SqliteSampleDatabaseBuilder.CreateAsync(
                root,
                "library.sqlite",
                sample,
                ["library.function_examples"]);

            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "library.sqlite")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM books; SELECT COUNT(*) FROM function_examples;";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(4L, reader.GetInt64(0));
                Assert.True(reader.NextResult());
                Assert.True(reader.Read());
                Assert.Equal(4L, reader.GetInt64(0));
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SqliteSampleDatabaseBuilder.CreateAsync(
                    root,
                    ":memory:",
                    sample,
                    sample.Objects.Select(item => item.Id)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FailedSampleCreationRollsBackTheLiveTransaction()
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-sample-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var brokenSample = new SqliteSamplePack(
            "broken",
            "Broken",
            "",
            [
                new("broken.table", "table", SqliteSampleObjectKind.Table, "CREATE TABLE broken_table (id INTEGER PRIMARY KEY);", null, []),
                new("broken.invalid", "invalid", SqliteSampleObjectKind.View, "CREATE TABLE broken_table (", null, ["broken.table"]),
            ]);

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                SqliteSampleDatabaseBuilder.CreateAsync(
                    root,
                    "broken.sqlite",
                    brokenSample,
                    brokenSample.Objects.Select(item => item.Id)));

            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "broken.sqlite")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'broken_table';";
                Assert.Equal(0L, (long)command.ExecuteScalar()!);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static (HashSet<string> Tables, HashSet<string> Views, HashSet<string> Triggers) ReadObjectNames(SqliteConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var views = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var triggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            switch (reader.GetString(0))
            {
                case "table":
                    tables.Add(reader.GetString(1));
                    break;
                case "view":
                    views.Add(reader.GetString(1));
                    break;
                case "trigger":
                    triggers.Add(reader.GetString(1));
                    break;
            }
        }

        return (tables, views, triggers);
    }
}
