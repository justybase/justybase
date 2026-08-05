using System.Data.Common;
using System.Text;
using JustyBase.Common.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using Microsoft.Data.Sqlite;
using Moq;

namespace JustyBase.Tests;

/// <summary>
/// Product-path e2e without Netezza: resolve via <see cref="DatabaseServiceHelpers"/>,
/// run SQL on real Sqlite, read results, export CSV.
/// </summary>
public sealed class SqliteProductPipelineTests
{
    [Fact]
    public async Task Connect_Run_Results_Export_Csv_WithoutNetezza()
    {
        string connectionName = $"sqlite-pipeline-{Guid.NewGuid():N}";
        string csvPath = Path.Combine(Path.GetTempPath(), $"jb-sqlite-pipeline-{Guid.NewGuid():N}.csv");

        using var sqlite = new SqliteConnection("Data Source=:memory:");
        sqlite.Open();

        using (var setup = sqlite.CreateCommand())
        {
            setup.CommandText = """
                CREATE TABLE products(id INTEGER PRIMARY KEY, name TEXT NOT NULL);
                INSERT INTO products(id, name) VALUES (1, 'justy'), (2, 'base');
                """;
            setup.ExecuteNonQuery();
        }

        var databaseService = new Mock<IDatabaseService>();
        databaseService.SetupAllProperties();
        databaseService.Setup(s => s.GetConnection(It.IsAny<string?>(), It.IsAny<bool>())).Returns(sqlite);
        databaseService.Setup(s => s.CreateCommandFromConnection(It.IsAny<DbConnection>()))
            .Returns((DbConnection c) => c.CreateCommand());
        databaseService.Setup(s => s.ClearCachedData());

        try
        {
            IDatabaseService? service = DatabaseServiceHelpers.GetDatabaseService(
                null,
                connectionName,
                ownDatabaseService: databaseService.Object);

            Assert.NotNull(service);
            Assert.Equal(connectionName, service.Name);
            Assert.Equal(DatabaseConnectedLevel.Connected, service.ConnectedLevel);
            Assert.True(DatabaseServiceHelpers.IsDatabaseConnected(connectionName));

            using DbConnection connection = service.GetConnection(null);
            Assert.Same(sqlite, connection);

            using (DbCommand query = service.CreateCommandFromConnection(connection))
            {
                query.CommandText = "SELECT id, name FROM products ORDER BY id;";
                using DbDataReader reader = query.ExecuteReader();

                Assert.True(reader.Read());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal("justy", reader.GetString(1));

                Assert.True(reader.Read());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal("base", reader.GetString(1));

                Assert.False(reader.Read());
            }

            using (DbCommand exportCmd = service.CreateCommandFromConnection(connection))
            {
                exportCmd.CommandText = "SELECT id, name FROM products ORDER BY id;";
                using DbDataReader exportReader = exportCmd.ExecuteReader();

                string written = await ExportDbReaderExtensions.HandleCsvOrParquetOutput(
                    exportReader,
                    csvPath,
                    new AdvancedExportOptions
                    {
                        Delimiter = '|',
                        LineDelimiter = "\n",
                        Header = true,
                        Encod = new UTF8Encoding(false),
                        CompresionType = CompressionEnum.None
                    },
                    progressAction: null);

                Assert.Equal(csvPath, written);
            }

            string csv = File.ReadAllText(csvPath);
            Assert.Contains("id|name", csv, StringComparison.Ordinal);
            Assert.Contains("1|justy", csv, StringComparison.Ordinal);
            Assert.Contains("2|base", csv, StringComparison.Ordinal);
        }
        finally
        {
            DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
            if (File.Exists(csvPath))
            {
                File.Delete(csvPath);
            }
        }
    }
}
