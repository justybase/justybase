using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;

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
