using AccessPlugin;
using JustyBase.Common.Tools;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.ImportExport.Import;
using ExcelPlugin;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using SpreadSheetTasks;
using System.Data;
using System.Data.Common;
using System.IO.Compression;

namespace JustyBase.Tests;

public sealed class FileDatabasePluginTests
{
    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsb")]
    public void ExcelExport_WritesResultSetsToSeparateSheets(string extension)
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-excel-export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string workbookPath = Path.Combine(root, $"results{extension}");

        try
        {
            using DataTable first = new("First");
            first.Columns.Add("Value", typeof(int));
            first.Rows.Add(1);

            using DataTable second = new("Second");
            second.Columns.Add("Value", typeof(int));
            second.Rows.Add(2);

            using (DbDataReader reader = new DataTableReader([first, second]))
            {
                reader.HandleExcelOutput(workbookPath, "SELECT 1; SELECT 2;", "Justy", null);
            }

            var factory = new SpreadSheetSourceFactory();
            using IImportSource source = factory.OpenSource(workbookPath, null);
            string[] sheetNames = source.GetSheetNames().ToArray();
            Assert.Contains("Sheet1", sheetNames);
            Assert.Contains("Sheet2", sheetNames);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsb")]
    public async Task ExcelPlugin_QueriesWorkbookAndCachesSheets(string extension)
    {
        string root = Path.Combine(Path.GetTempPath(), $"justybase-excel-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string workbookPath = Path.Combine(root, $"orders{extension}");

        try
        {
            using DataTable data = new("Orders");
            data.Columns.Add("Order Id", typeof(int));
            data.Columns.Add("Amount", typeof(decimal));
            data.Columns.Add("Is Paid", typeof(bool));
            data.Rows.Add(1, 12.50m, true);
            data.Rows.Add(2, 7.25m, false);

            using (var workbook = new MemoryStream())
            {
                using (ExcelWriter writer = extension == ".xlsx"
                           ? new XlsxWriter(workbook, 4096, true, true, CompressionLevel.Optimal, leaveExcelArchiveOpen: true)
                           : new XlsbWriter(workbook, CompressionLevel.Optimal, leaveExcelArchiveOpen: true))
                {
                    writer.AddSheet("Orders");
                    writer.WriteSheet(data);
                }

                File.WriteAllBytes(workbookPath, workbook.ToArray());
            }

            var service = new Excel(string.Empty, string.Empty, string.Empty, string.Empty, workbookPath, 15)
            {
                TempDataDirectory = root
            };

            await using (DbConnection connection = service.GetConnection(null, pooling: false))
            {
                await connection.OpenAsync();
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = "SELECT \"Order_Id\", \"Amount\", \"Is_Paid\" FROM \"Orders\" ORDER BY \"Order_Id\"";
                await using DbDataReader reader = await command.ExecuteReaderAsync();

                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, Convert.ToInt64(reader.GetValue(0)));
                Assert.Equal(12.50m, Convert.ToDecimal(reader.GetValue(1)));
                Assert.True(Convert.ToBoolean(reader.GetValue(2)));
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, Convert.ToInt64(reader.GetValue(0)));
                Assert.False(await reader.ReadAsync());
            }

            service.CacheMainDictionary();
            string database = service.GetDatabases(string.Empty).Single();
            DatabaseObject table = Assert.Single(service.GetDbObjects(database, "main", "Orders", TypeInDatabaseEnum.Table));
            Assert.Equal("Orders", table.Name);
            DatabaseColumn[] columns = service.GetColumns(database, "main", "Orders", string.Empty).ToArray();
            Assert.Equal(3, columns.Length);
            Assert.Equal(["ORDER_ID", "AMOUNT", "IS_PAID"], columns.Select(column => column.Name).ToArray());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void AccessPlugin_GeneratesAccessTopSyntaxWithoutSyntheticSchema()
    {
        var service = new Access(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 15);

        Assert.Equal(
            "SELECT TOP 100 * FROM [t_people];",
            service.GetTop100Select("database", "MAIN", "t_people", snippetMode: false));
        Assert.Equal("[customer]]orders]", service.QuoteNameIfNeeded("customer]orders"));
    }

    [Fact]
    public async Task AccessPlugin_QueriesLocalUCanAccessFixtureAndCachesMetadata()
    {
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "JustyBase.UCanAccessCs", "samples", "sample2003.mdb"));
        if (!File.Exists(fixture))
        {
            // The provider repository is intentionally a sibling checkout and is not
            // required for consumers that use the packaged plugin binaries.
            return;
        }

        var service = new Access(string.Empty, string.Empty, string.Empty, string.Empty, fixture, 15)
        {
            TempDataDirectory = Path.GetTempPath()
        };
        service.ApplyLoginData(new LoginDataModel
        {
            ConnectionName = "fixture",
            Driver = "Access",
            Database = fixture,
            AccessOptions = new AccessConnectionOptions { ReadOnly = true }
        });

        await using (DbConnection connection = service.GetConnection(null, pooling: false))
        {
            await connection.OpenAsync();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM [t_people]";
            Assert.Equal(3L, Convert.ToInt64(await command.ExecuteScalarAsync()));

            await using DbCommand topCommand = connection.CreateCommand();
            topCommand.CommandText = service.GetTop100Select(fixture, "MAIN", "t_people", snippetMode: false);
            await using DbDataReader topReader = await topCommand.ExecuteReaderAsync();
            Assert.True(await topReader.ReadAsync());
        }

        service.CacheMainDictionary();
        string database = service.GetDatabases(string.Empty).Single();
        DatabaseObject table = Assert.Single(service.GetDbObjects(database, "MAIN", "t_people", TypeInDatabaseEnum.Table));
        Assert.Equal("t_people", table.Name);
        Assert.Contains(service.GetColumns(database, "MAIN", "t_people", string.Empty), column => column.Name == "name");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Native workbook readers can release a file handle shortly after Dispose.
        }
    }
}
