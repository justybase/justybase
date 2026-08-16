using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.Common.Tools;
using JustyBase.Helpers;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.SqliteDriver;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace JustyBase.Tests;

public sealed class SqliteDriverLiveTests
{
    [Theory]
    [InlineData(":memory:")]
    [InlineData("file::memory:?cache=shared")]
    public async Task InMemoryConnectionsRemainUsableAfterCallerDisposesConnection(string dataSource)
    {
        var service = new Sqlite(string.Empty, string.Empty, string.Empty, string.Empty, dataSource, 10);

        using (DbConnection setup = service.GetConnection(null, pooling: false))
        {
            setup.Open();
            using DbCommand command = setup.CreateCommand();
            command.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO items VALUES (1, 'one');";
            command.ExecuteNonQuery();
        }

        using (DbConnection query = service.GetConnection(null, pooling: false))
        {
            query.Open();
            using DbCommand command = query.CreateCommand();
            command.CommandText = "SELECT name FROM items WHERE id = 1;";
            Assert.Equal("one", command.ExecuteScalar());
        }

        string ddl = await service.GetCreateTableText(string.Empty, "main", "items");
        Assert.Contains("CREATE TABLE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileBackedSchemaCompletionAndCsvImportUseLiveSqlite()
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string databaseFile = Path.Combine(root, "live.sqlite");
        string csvFile = Path.Combine(root, "import.csv");
        string exportFile = Path.Combine(root, "export.csv");
        string databaseName = Path.GetFileName(databaseFile);

        try
        {
            var service = new Sqlite("", "", "", root, databaseName, 10);
            using (DbConnection setup = service.GetConnection(null, pooling: false))
            {
                setup.Open();
                using DbCommand command = setup.CreateCommand();
                command.CommandText = """
                    CREATE TABLE people (
                        id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL,
                        amount NUMERIC,
                        created TEXT
                    );
                    CREATE VIEW people_view AS SELECT id, name FROM people;
                    """;
                command.ExecuteNonQuery();
            }

            service.CacheMainDictionary();

            var tables = service.GetDbObjects(databaseName, "main", "pe", TypeInDatabaseEnum.Table)
                .Select(item => item.Name)
                .ToList();
            Assert.Contains("people", tables);

            var views = service.GetDbObjects(databaseName, "main", "people", TypeInDatabaseEnum.View)
                .Select(item => item.Name)
                .ToList();
            Assert.Contains("people_view", views);

            var columns = service.GetColumns(databaseName, "main", "people", "na")
                .ToList();
            Assert.Equal(["name"], columns.Select(column => column.Name));
            Assert.True(columns.Single().ColumnNotNull);

            string createFromDdl = service.GetCreateFromCode("main.people");
            Assert.DoesNotContain("DISTRIBUTE ON RANDOM", createFromDdl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CREATE TABLE ABC AS SELECT", createFromDdl, StringComparison.OrdinalIgnoreCase);

            using (DbConnection createFromConnection = service.GetConnection(null, pooling: false))
            {
                createFromConnection.Open();
                using DbCommand createFrom = createFromConnection.CreateCommand();
                createFrom.CommandText = createFromDdl;
                createFrom.ExecuteNonQuery();
            }

            using (DbConnection verifyCreateFrom = service.GetConnection(null, pooling: false))
            {
                verifyCreateFrom.Open();
                using DbCommand createFromVerifyCommand = verifyCreateFrom.CreateCommand();
                createFromVerifyCommand.CommandText = "SELECT COUNT(*) FROM main.ABC;";
                Assert.Equal(0L, (long)createFromVerifyCommand.ExecuteScalar()!);
            }

            var amountColumn = service.GetColumns(databaseName, "main", "people", "")
                .Single(column => column.Name == "amount");
            Assert.False(amountColumn.ColumnNotNull);

            var autocomplete = new AutocompleteService();
            var tableCompletions = autocomplete.GetWordsList(
                    "main.pe",
                    new Dictionary<string, List<string>>(),
                    new Dictionary<string, List<string>>(),
                    new Dictionary<string, List<string>>(),
                    new Dictionary<string, List<string>>(),
                    service,
                    databaseName)
                .Select(item => item.Text)
                .ToList();
            Assert.Contains("people", tableCompletions);

            var columnCompletions = autocomplete.GetWordsList(
                    "p.na",
                    new Dictionary<string, List<string>> { ["main.people"] = ["p"] },
                    new Dictionary<string, List<string>>(),
                    new Dictionary<string, List<string>>(),
                    new Dictionary<string, List<string>>(),
                    service,
                    databaseName)
                .Select(item => item.Text)
                .ToList();
            Assert.Contains("name", columnCompletions);
            Assert.Equal(SqlDialect.Sqlite, SqlDialectResolver.ForDatabaseType(DatabaseTypeEnum.Sqlite));
            Assert.Contains("ATTACH", DialectRuntime.AuthoringCatalog(SqlDialect.Sqlite).CompletionKeywords);

            string tableDdl = await service.GetCreateTableText(databaseName, "main", "people");
            Assert.Contains("CREATE TABLE", tableDdl, StringComparison.OrdinalIgnoreCase);
            string viewDdl = await service.GetCreateViewText(databaseName, "main", "people_view");
            Assert.Contains("CREATE VIEW", viewDdl, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(csvFile, "id,name,amount,created\n1,alpha,12.50,2026-08-16T12:00:00\n2,\"beta, two\",7.25,2026-08-17T13:30:00\n", new UTF8Encoding(false));
            using var csvReader = new CsvReader();
            csvReader.Open(csvFile);
            var chooser = new DatabaseTypeChooser
            {
                NormalizedColumnHeaderNames = ["id", "name", "amount", "created"],
                ColumnTypesBestMatch =
                [
                    new DbTypeWithSize(DbSimpleType.Integer),
                    new DbTypeWithSize(DbSimpleType.Nvarchar) { TextLength = 100 },
                    new DbTypeWithSize(DbSimpleType.Numeric) { NumericPrecision = 12, NumericScale = 2 },
                    new DbTypeWithSize(DbSimpleType.TimeStamp),
                ]
            };
            var importColumns = DatabaseTypeChooser.ToImportColumns(
                chooser.NormalizedColumnHeaderNames,
                chooser.ColumnTypesBestMatch);
            using var importReader = new DataReaderFromExcelReaderAbstract(
                csvReader,
                importColumns.Select(column => column.Kind).ToArray(),
                chooser.NormalizedColumnHeaderNames);
            var importJob = new DbImportJob(importReader, chooser);
            await service.DbSpecificImportPart(importJob, "main.imported", null);

            using DbConnection verify = service.GetConnection(null, pooling: false);
            verify.Open();
            using DbCommand verifyCommand = verify.CreateCommand();
            verifyCommand.CommandText = "SELECT id, name, amount FROM main.imported ORDER BY id;";
            using DbDataReader reader = verifyCommand.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal("alpha", reader.GetString(1));
            Assert.Equal(12.5, Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture));
            Assert.True(reader.Read());
            Assert.Equal("beta, two", reader.GetString(1));
            Assert.False(reader.Read());

            using DbCommand exportCommand = verify.CreateCommand();
            exportCommand.CommandText = "SELECT id, name, amount FROM main.imported ORDER BY id;";
            using DbDataReader exportReader = exportCommand.ExecuteReader();
            string written = await ExportDbReaderExtensions.HandleCsvOrParquetOutput(
                exportReader,
                exportFile,
                new AdvancedExportOptions
                {
                    Delimiter = '|',
                    LineDelimiter = "\n",
                    Header = true,
                    Encod = new UTF8Encoding(false),
                    CompresionType = CompressionEnum.None
                },
                progressAction: null);

            Assert.Equal(exportFile, written);
            string exported = File.ReadAllText(exportFile);
            Assert.Contains("id|name|amount", exported, StringComparison.Ordinal);
            Assert.Contains("2|beta, two|7.25", exported, StringComparison.Ordinal);
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
    public async Task MetadataCacheIncludesIndexesTriggersForeignKeysPrimaryKeysAndAttachedCatalogs()
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-sqlite-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string databaseFile = Path.Combine(root, "main.sqlite");
        string attachedFile = Path.Combine(root, "audit.sqlite");
        string databaseName = Path.GetFileName(databaseFile);

        try
        {
            var attachedService = new Sqlite("", "", "", "", attachedFile, 10);
            using (DbConnection attachedConnection = attachedService.GetConnection(null, pooling: false))
            {
                attachedConnection.Open();
                using DbCommand command = attachedConnection.CreateCommand();
                command.CommandText = "CREATE TABLE audit_log (event_id INTEGER PRIMARY KEY, message TEXT);";
                command.ExecuteNonQuery();
            }

            var service = new Sqlite("", "", "", root, databaseName, 10)
            {
                ConnectionOptions = new SqliteConnectionOptions
                {
                    BusyTimeoutMilliseconds = 4321,
                    AttachedDatabases =
                    [
                        new SqliteAttachedDatabaseOptions
                        {
                            Alias = "audit",
                            FilePath = attachedFile,
                            ReadOnly = true
                        }
                    ]
                }
            };

            using (DbConnection setup = service.GetConnection(null, pooling: false))
            {
                setup.Open();
                service.ConfigureOpenConnection(setup);
                using (DbCommand sessionInfo = setup.CreateCommand())
                {
                    sessionInfo.CommandText = "PRAGMA busy_timeout; PRAGMA foreign_keys;";
                    using DbDataReader sessionReader = sessionInfo.ExecuteReader();
                    Assert.True(sessionReader.Read());
                    Assert.Equal(4321L, sessionReader.GetInt64(0));
                    Assert.True(sessionReader.NextResult());
                    Assert.True(sessionReader.Read());
                    Assert.Equal(1L, sessionReader.GetInt64(0));
                }

                using (DbCommand readOnlyProbe = setup.CreateCommand())
                {
                    readOnlyProbe.CommandText = "CREATE TABLE audit.write_probe (id INTEGER);";
                    Assert.ThrowsAny<DbException>(() => readOnlyProbe.ExecuteNonQuery());
                }

                using DbCommand command = setup.CreateCommand();
                command.CommandText = """
                    CREATE TABLE parent (
                        id INTEGER PRIMARY KEY,
                        code TEXT NOT NULL UNIQUE
                    ) STRICT;
                    CREATE TABLE child (
                        id INTEGER PRIMARY KEY,
                        parent_id INTEGER NOT NULL,
                        value TEXT,
                        FOREIGN KEY(parent_id) REFERENCES parent(id)
                    );
                    CREATE INDEX idx_child_value ON child(value DESC) WHERE value IS NOT NULL;
                    CREATE TRIGGER child_after_insert
                    AFTER INSERT ON child
                    BEGIN
                        UPDATE parent SET code = code WHERE id = NEW.parent_id;
                    END;
                    CREATE VIEW child_view AS SELECT id, value FROM child;
                    CREATE VIRTUAL TABLE child_fts USING fts5(value);
                    """;
                command.ExecuteNonQuery();
            }

            service.CacheMainDictionary();

            Assert.Contains("main", service.GetSchemas(databaseName, ""));
            Assert.Contains("audit", service.GetSchemas(databaseName, ""));

            var indexes = service.GetDbObjects(databaseName, "main", "", TypeInDatabaseEnum.Index).ToList();
            var index = Assert.Single(indexes, item => item.Name == "idx_child_value");
            Assert.Equal(TypeInDatabaseEnum.Index, index.TypeInDatabase);
            Assert.Equal("child", index.ParentObjectName);
            Assert.Contains("partial", index.Desc, StringComparison.OrdinalIgnoreCase);

            var triggers = service.GetDbObjects(databaseName, "main", "", TypeInDatabaseEnum.Trigger).ToList();
            var trigger = Assert.Single(triggers, item => item.Name == "child_after_insert");
            Assert.Equal("child", trigger.ParentObjectName);
            Assert.Contains("trigger", trigger.Desc, StringComparison.OrdinalIgnoreCase);

            var primaryKeyColumns = service.GetColumns(databaseName, "main", "child", "")
                .Where(column => column.IsPrimaryKey)
                .ToList();
            Assert.Equal(["id"], primaryKeyColumns.Select(column => column.Name));
            Assert.Equal(1, primaryKeyColumns.Single().PrimaryKeyOrdinal);

            var snapshot = service.GetSchemaSnapshot(databaseName, "main");
            Assert.Contains(snapshot.Tables, table => table.Name == "parent" && table.Strict);
            Assert.Contains(snapshot.Tables, table => table.Name == "child_fts" && table.Module == "fts5");
            Assert.Contains(snapshot.Indexes, item => item.Name == "idx_child_value" && item.IsPartial);
            Assert.Contains(snapshot.ForeignKeys["child"], item => item.ReferencedTable == "parent");

            var attachedTables = service.GetDbObjects(databaseName, "audit", "", TypeInDatabaseEnum.Table).ToList();
            Assert.Contains(attachedTables, item => item.Name == "audit_log");

            string triggerDdl = await service.GetCreateTriggerText(databaseName, "main", "child_after_insert");
            Assert.Contains("CREATE TRIGGER", triggerDdl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UPDATE parent", triggerDdl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

}
