using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using System.Data;

namespace JustyBase.IntegrationTests;

/// <summary>
/// Live Netezza round-trip proof: CSV → app import pipeline → SELECT back and compare
/// every cell, plus the actual created column types. Requires NZ_DEV_* (run via
/// scripts/test-netezza-integration.ps1 or workflow_dispatch).
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetezzaImportRoundTripTests
{
    [Fact]
    public async Task BasicTypes_ImportAndSelectBack_AllCellsMatch()
    {
        const string csv = """
            id,price,name,d,flag
            1,10.50,alpha,2024-01-15 10:30:00,true
            2,20.75,beta,2024-02-20 08:05:30,false
            3,1.25,gamma,2024-03-01 23:59:59,true
            """;

        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv);

        Assert.Equal(DbSimpleType.Integer, ctx.Types[0].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Numeric, ctx.Types[1].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[2].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[3].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Boolean, ctx.Types[4].DatabaseTypeSimple);

        object?[][] expected =
        [
            [1L, 10.50m, "alpha", new DateTime(2024, 1, 15, 10, 30, 0), true],
            [2L, 20.75m, "beta", new DateTime(2024, 2, 20, 8, 5, 30), false],
            [3L, 1.25m, "gamma", new DateTime(2024, 3, 1, 23, 59, 59), true]
        ];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "BasicTypes");

        NetezzaImportRoundTripRunner.VerifyColumnFormats(ctx, new Dictionary<string, Func<string, bool>>
        {
            ["ID"] = f => f.Equals("BIGINT", StringComparison.OrdinalIgnoreCase),
            ["PRICE"] = f => f.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase),
            ["NAME"] = f => IsNvarchar(f),
            ["D"] = f => f.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase),
            ["FLAG"] = f => f.Contains("BOOL", StringComparison.OrdinalIgnoreCase)
        });
    }

    private static bool IsNvarchar(string formatType)
        => formatType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase)
           || formatType.StartsWith("NATIONAL CHARACTER VARYING", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task UserOverride_NumericToText_DataPreservedAsText()
    {
        const string csv = "id,price\n1,10.5\n2,20.75\n3,1.25\n";
        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv, chooser =>
        {
            Assert.Equal(DbSimpleType.Numeric, chooser.ColumnTypesBestMatch![1].DatabaseTypeSimple);
            chooser.ColumnTypesBestMatch[1] = new DbTypeWithSize(DbSimpleType.Nvarchar)
            {
                TextLength = DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH
            };
        });

        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[1].DatabaseTypeSimple);

        object?[][] expected = [[1L, "10.5"], [2L, "20.75"], [3L, "1.25"]];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "NumericToText");
        NetezzaImportRoundTripRunner.VerifyColumnFormats(ctx, new Dictionary<string, Func<string, bool>>
        {
            ["PRICE"] = f => IsNvarchar(f)
        });
    }

    [Fact]
    public async Task UserOverride_TimeStampToDate_DataPreservedAsDates()
    {
        const string csv = "d\n2024-01-15 10:30:00\n2024-02-20 08:05:30\n";
        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv, chooser =>
        {
            Assert.Equal(DbSimpleType.TimeStamp, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            chooser.ColumnTypesBestMatch[0] = new DbTypeWithSize(DbSimpleType.Date);
        });

        Assert.Equal(DbSimpleType.Date, ctx.Types[0].DatabaseTypeSimple);

        object?[][] expected = [[new DateTime(2024, 1, 15)], [new DateTime(2024, 2, 20)]];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "TimeStampToDate");
        NetezzaImportRoundTripRunner.VerifyColumnFormats(ctx, new Dictionary<string, Func<string, bool>>
        {
            ["D"] = f => f.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
        });
    }

    [Fact]
    public async Task EdgeCases_NullsSpecialCharsUnicodeAndLongText_RoundTrip()
    {
        string longText = new('x', 400);
        string csv =
            "id,text\n" +
            "1,\"a,b\"\n" +
            "2,\"say \"\"hi\"\"\"\n" +
            "3,\"line1\nline2\"\n" +
            "4,\"a\\b\"\n" +
            "5,\"tab\there\"\n" +
            "6,\"żółć 中文 ✓\"\n" +
            "7,\n" +
            $"8,\"{longText}\"\n";

        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv);

        Assert.Equal(DbSimpleType.Integer, ctx.Types[0].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[1].DatabaseTypeSimple);

        object?[][] expected =
        [
            [1L, "a,b"],
            [2L, "say \"hi\""],
            [3L, "line1\nline2"],
            [4L, "a\\b"],
            [5L, "tab\there"],
            [6L, "żółć 中文 ✓"],
            [7L, null],
            [8L, longText]
        ];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "EdgeCases");
    }

    [Fact]
    public async Task AllEmptyCsvColumn_ImportsAsNvarcharNulls()
    {
        const string csv = "id,note\n1,\n2,\n3,\n";
        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv);

        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[1].DatabaseTypeSimple);

        object?[][] expected = [[1L, null], [2L, null], [3L, null]];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "AllEmptyCsvColumn");
        NetezzaImportRoundTripRunner.VerifyColumnFormats(ctx, new Dictionary<string, Func<string, bool>>
        {
            ["NOTE"] = f => IsNvarchar(f)
        });
    }

    [Fact]
    public async Task ExistingTable_ImportIntoPreexistingColumns_RoundTrips()
    {
        const string csv = "id,amount,label\n1,10.5,alpha\n2,20.75,beta\n3,1.25,gamma\n";

        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvIntoExistingAsync(
            csv,
            "CREATE TABLE {0} (ID BIGINT, AMOUNT NUMERIC(16,2), LABEL NVARCHAR(50)) DISTRIBUTE ON RANDOM",
            ["ID", "AMOUNT", "LABEL"]);

        Assert.Equal(DbSimpleType.Integer, ctx.Types[0].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Numeric, ctx.Types[1].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[2].DatabaseTypeSimple);

        object?[][] expected = [[1L, 10.5m, "alpha"], [2L, 20.75m, "beta"], [3L, 1.25m, "gamma"]];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "ExistingTable");
        NetezzaImportRoundTripRunner.VerifyColumnFormats(ctx, new Dictionary<string, Func<string, bool>>
        {
            ["ID"] = f => f.Equals("BIGINT", StringComparison.OrdinalIgnoreCase),
            ["AMOUNT"] = f => f.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase),
            ["LABEL"] = f => IsNvarchar(f)
        });
    }

    [Fact]
    public async Task Xlsx_BasicTypes_ImportAndSelectBack_AllCellsMatch()
    {
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        dt.Columns.Add("price", typeof(double));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("d", typeof(DateTime));
        dt.Columns.Add("flag", typeof(bool));
        dt.Rows.Add(1L, 10.5, "alpha", new DateTime(2024, 1, 15, 10, 30, 0), true);
        dt.Rows.Add(2L, 20.75, "beta", new DateTime(2024, 2, 20, 8, 5, 30), false);
        dt.Rows.Add(3L, 1.25, "gamma", new DateTime(2024, 3, 1, 23, 59, 59), true);

        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportXlsxAsync(dt);
        dt.Dispose();

        Assert.Equal(DbSimpleType.Integer, ctx.Types[0].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Numeric, ctx.Types[1].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[2].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[3].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Boolean, ctx.Types[4].DatabaseTypeSimple);

        object?[][] expected =
        [
            [1L, 10.5m, "alpha", new DateTime(2024, 1, 15, 10, 30, 0), true],
            [2L, 20.75m, "beta", new DateTime(2024, 2, 20, 8, 5, 30), false],
            [3L, 1.25m, "gamma", new DateTime(2024, 3, 1, 23, 59, 59), true]
        ];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "XlsxBasicTypes");
    }

    [Fact]
    public async Task Xlsx_MidnightDateColumn_ImportsAllRows()
    {
        // Regression for the "date but no time" rejection: an Excel date column whose values are
        // all at midnight must stream the full timestamp form into the TIMESTAMP column.
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        dt.Columns.Add("d", typeof(DateTime));
        dt.Rows.Add(1L, new DateTime(2024, 1, 15));
        dt.Rows.Add(2L, new DateTime(2024, 2, 20));
        dt.Rows.Add(3L, new DateTime(2024, 3, 1));

        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportXlsxAsync(dt);
        dt.Dispose();

        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[1].DatabaseTypeSimple);

        object?[][] expected =
        [
            [1L, new DateTime(2024, 1, 15)],
            [2L, new DateTime(2024, 2, 20)],
            [3L, new DateTime(2024, 3, 1)]
        ];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "XlsxMidnightDate");
    }

    [Fact]
    public async Task Csv_MidnightTimeStamp_ImportsAllRows()
    {
        // A timestamp column with midnight values must keep the time part on the pipe.
        const string csv = "d\n2024-01-15 00:00:00\n2024-02-20 00:00:00\n2024-03-01 00:00:00\n";
        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv);

        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[0].DatabaseTypeSimple);

        object?[][] expected =
        [
            [new DateTime(2024, 1, 15)],
            [new DateTime(2024, 2, 20)],
            [new DateTime(2024, 3, 1)]
        ];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, "CsvMidnightTimestamp");
    }

    [Fact]
    public async Task Xlsx_DiverseTypes_ImportAndSelectBack_AllCellsMatch()
        => await DiverseTypesRoundTripAsync(writeXlsb: false, "XlsxDiverse");

    [Fact]
    public async Task Xlsb_DiverseTypes_ImportAndSelectBack_AllCellsMatch()
        => await DiverseTypesRoundTripAsync(writeXlsb: true, "XlsbDiverse");

    private static async Task DiverseTypesRoundTripAsync(bool writeXlsb, string caseName)
    {
        string longTextX = new('x', 400);
        string longTextUnicode = new('ą', 400);
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        dt.Columns.Add("amount", typeof(decimal));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("d", typeof(DateTime)); // date-only (midnight) values
        dt.Columns.Add("ts", typeof(DateTime)); // timestamps incl. one midnight value
        dt.Columns.Add("flag", typeof(bool));
        dt.Columns.Add("note", typeof(string)); // nulls and text
        dt.Columns.Add("longtext", typeof(string));
        dt.Rows.Add(1L, 10.50m, "alpha", new DateTime(2024, 1, 15), new DateTime(2024, 1, 15, 10, 30, 0), true, "hello", longTextX);
        dt.Rows.Add(2L, 20.75m, "żółć 中文 ✓", new DateTime(2024, 2, 20), new DateTime(2024, 2, 20, 8, 5, 30), false, null, longTextUnicode);
        dt.Rows.Add(3L, 1.25m, "tab\there \"q\"", new DateTime(2024, 3, 1), new DateTime(2024, 3, 1, 23, 59, 59), true, "a,b", "long");
        dt.Rows.Add(4L, 0.0m, "beta", new DateTime(2024, 4, 5), new DateTime(2024, 4, 5, 0, 0, 0), false, "x", new string('z', 400));

        await using RoundTripContext ctx = writeXlsb
            ? await NetezzaImportRoundTripRunner.ImportXlsbAsync(dt)
            : await NetezzaImportRoundTripRunner.ImportXlsxAsync(dt);
        dt.Dispose();

        Assert.Equal(DbSimpleType.Integer, ctx.Types[0].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Numeric, ctx.Types[1].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[2].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[3].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[4].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Boolean, ctx.Types[5].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[6].DatabaseTypeSimple);
        Assert.Equal(DbSimpleType.Nvarchar, ctx.Types[7].DatabaseTypeSimple);

        object?[][] expected =
        [
            [1L, 10.5m, "alpha", new DateTime(2024, 1, 15), new DateTime(2024, 1, 15, 10, 30, 0), true, "hello", longTextX],
            [2L, 20.75m, "żółć 中文 ✓", new DateTime(2024, 2, 20), new DateTime(2024, 2, 20, 8, 5, 30), false, null, longTextUnicode],
            [3L, 1.25m, "tab\there \"q\"", new DateTime(2024, 3, 1), new DateTime(2024, 3, 1, 23, 59, 59), true, "a,b", "long"],
            [4L, 0.0m, "beta", new DateTime(2024, 4, 5), new DateTime(2024, 4, 5, 0, 0, 0), false, "x", new string('z', 400)]
        ];
        NetezzaImportRoundTripRunner.VerifyRows(ctx, expected, caseName);
    }

    [Fact]
    public async Task Xlsx_LargeImport_AllRowsLoad()
        => await LargeImportRoundTripAsync(writeXlsb: false, "XlsxLarge");

    [Fact]
    public async Task Xlsb_LargeImport_AllRowsLoad()
        => await LargeImportRoundTripAsync(writeXlsb: true, "XlsbLarge");

    private static async Task LargeImportRoundTripAsync(bool writeXlsb, string caseName)
    {
        const int rowCount = 20_000;
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        dt.Columns.Add("amount", typeof(decimal));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("ts", typeof(DateTime));
        for (int i = 0; i < rowCount; i++)
        {
            dt.Rows.Add(i + 1L, (i % 100) / 10.0m, $"row-{i}", new DateTime(2024, 1, 1).AddMinutes(i));
        }

        await using RoundTripContext ctx = writeXlsb
            ? await NetezzaImportRoundTripRunner.ImportXlsbAsync(dt)
            : await NetezzaImportRoundTripRunner.ImportXlsxAsync(dt);
        dt.Dispose();

        long actual = Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(
            ctx.Connection,
            $"SELECT COUNT(*) FROM {ctx.TableName}"),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(rowCount, (int)actual);

        object? first = NetezzaLiveTestHost.ExecuteScalar(ctx.Connection, $"SELECT MIN({ctx.Columns[0]}) FROM {ctx.TableName}");
        object? last = NetezzaLiveTestHost.ExecuteScalar(ctx.Connection, $"SELECT MAX({ctx.Columns[0]}) FROM {ctx.TableName}");
        Assert.Equal(1L, Convert.ToInt64(first, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(rowCount, (int)Convert.ToInt64(last, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Xlsx_RealWorld200kFile_ImportsAllRows()
    {
        // The exact file that originally imported "successfully" with 0 rows: a 200k-row sheet whose
        // date column holds midnight values (Netezza rejected "date but no time" for the TIMESTAMP
        // column). Skipped when the sample file is not present on the machine.
        const string samplePath = @"C:\DEV\DEV\Others\sqls\fileLowMemory.xlsx";
        if (!File.Exists(samplePath))
        {
            return;
        }

        await using RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportExistingFileAsync(samplePath);

        Assert.Equal(4, ctx.Columns.Length);
        Assert.Equal(DbSimpleType.TimeStamp, ctx.Types[2].DatabaseTypeSimple);

        long actual = Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(
            ctx.Connection,
            $"SELECT COUNT(*) FROM {ctx.TableName}"),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(200_000, (int)actual);

        long nonNullDates = Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(
            ctx.Connection,
            $"SELECT COUNT({ctx.Columns[2]}) FROM {ctx.TableName}"),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(200_000, (int)nonNullDates);
    }

    [Fact]
    public async Task ConcurrentImports_MultipleParallelRoundTrips_AllProduceCorrectData()
    {
        string[] csvs =
        [
            "id,val\n1,10\n2,20\n3,30\n",
            "id,val\n1,1.5\n2,2.5\n3,3.5\n4,4.5\n",
            "id,val\n5,true\n6,false\n"
        ];
        object?[][][] expectedRows =
        [
            [[1L, 10L], [2L, 20L], [3L, 30L]],
            [[1L, 1.5m], [2L, 2.5m], [3L, 3.5m], [4L, 4.5m]],
            [[5L, true], [6L, false]]
        ];

        RoundTripContext[] contexts = new RoundTripContext[csvs.Length];
        try
        {
            var tasks = csvs.Select((csv, i) => ImportAndCaptureAsync(csv, i, contexts));
            await Task.WhenAll(tasks);

            for (int i = 0; i < contexts.Length; i++)
            {
                NetezzaImportRoundTripRunner.VerifyRows(contexts[i], expectedRows[i], $"Concurrent#{i}");
            }
        }
        finally
        {
            foreach (RoundTripContext ctx in contexts)
            {
                if (ctx is not null)
                {
                    await ctx.DisposeAsync();
                }
            }
        }
    }

    private static async Task ImportAndCaptureAsync(string csv, int index, RoundTripContext[] slots)
    {
        RoundTripContext ctx = await NetezzaImportRoundTripRunner.ImportCsvAsync(csv);
        slots[index] = ctx;
    }
}
