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

}
