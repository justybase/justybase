using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Tests;

/// <summary>
/// Offline proof that the import type model maps to correct Netezza DDL
/// (the exact text used by CREATE TABLE during import). No live database required.
/// </summary>
public sealed class NetezzaImportDdlMatrixTests
{
    [Theory]
    [InlineData(DbSimpleType.Integer, null, 0, 0, "BIGINT")]
    [InlineData(DbSimpleType.Numeric, null, 20, 6, "NUMERIC(20,6)")]
    [InlineData(DbSimpleType.Numeric, null, 10, 2, "NUMERIC(10,2)")]
    [InlineData(DbSimpleType.Nvarchar, 255, 0, 0, "NVARCHAR(255)")]
    [InlineData(DbSimpleType.Nvarchar, 1000, 0, 0, "NVARCHAR(1000)")]
    [InlineData(DbSimpleType.Date, null, 0, 0, "DATE")]
    [InlineData(DbSimpleType.TimeStamp, null, 0, 0, "TIMESTAMP")]
    [InlineData(DbSimpleType.Boolean, null, 0, 0, "BOOL")]
    [InlineData(DbSimpleType.NoInfo, 255, 0, 0, "NVARCHAR(255)")]
    public void ToString_NetezzaSql_ProducesExpectedDdl(DbSimpleType simple, int? textLength, int precision, int scale, string expected)
    {
        DbTypeWithSize type = BuildType(simple, textLength, precision, scale);
        Assert.Equal(expected, type.ToString(DatabaseTypeEnum.NetezzaSQL));
    }

    [Theory]
    [InlineData(DbSimpleType.Integer, null, 0, 0, "INTEGER")]
    [InlineData(DbSimpleType.Numeric, null, 20, 6, "NUMBER (20,6)")]
    [InlineData(DbSimpleType.Nvarchar, 255, 0, 0, "VARCHAR2(255)")]
    public void ToString_Oracle_ProducesExpectedDdl(DbSimpleType simple, int? textLength, int precision, int scale, string expected)
    {
        DbTypeWithSize type = BuildType(simple, textLength, precision, scale);
        Assert.Equal(expected, type.ToString(DatabaseTypeEnum.Oracle));
    }

    [Fact]
    public void TypeChoice_ToDbTypeWithSize_PreservesDetectedSizesWhenTypeMatches()
    {
        DbTypeWithSize numeric = new(DbSimpleType.Numeric) { NumericPrecision = 14, NumericScale = 3 };

        DbTypeWithSize changed = TypeChoice.ToDbTypeWithSize(DbSimpleType.Numeric, numeric);

        Assert.Equal(14, changed.NumericPrecision);
        Assert.Equal(3, changed.NumericScale);
    }

    [Fact]
    public void TypeChoice_ToDbTypeWithSize_DefaultSizesWhenSwitchingToTextOrNumeric()
    {
        DbTypeWithSize integer = new(DbSimpleType.Integer);

        DbTypeWithSize asText = TypeChoice.ToDbTypeWithSize(DbSimpleType.Nvarchar, integer);
        Assert.Equal(DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH, asText.TextLength);

        DbTypeWithSize asNumeric = TypeChoice.ToDbTypeWithSize(DbSimpleType.Numeric, integer);
        Assert.Equal(20, asNumeric.NumericPrecision);
        Assert.Equal(6, asNumeric.NumericScale);
    }

    private static DbTypeWithSize BuildType(DbSimpleType simple, int? textLength, int precision, int scale)
    {
        DbTypeWithSize type = new(simple);
        if (textLength is not null)
        {
            type = type with { TextLength = textLength.Value };
        }
        return simple switch
        {
            DbSimpleType.Numeric => type with { NumericPrecision = precision, NumericScale = scale },
            _ => type
        };
    }
}

/// <summary>
/// Offline type-detection edge cases on real CSV content, plus the DDL that the
/// resulting job would generate. Mirrors what the Type selection tab shows pre-import.
/// </summary>
public sealed class NetezzaImportDetectionTests
{
    [Fact]
    public async Task DetectSheet_IntegersOnly_ProducesBigIntDdl()
    {
        await WithCsvAsync("id,count\n1,10\n2,20\n3,30\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Integer, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            Assert.Equal(DbSimpleType.Integer, chooser.ColumnTypesBestMatch[1].DatabaseTypeSimple);

            string[] ddl = await ReturnHeadersAsync(import);
            Assert.Equal("ID BIGINT", ddl[0]);
            Assert.Equal("COUNT BIGINT", ddl[1]);
        });
    }

    [Fact]
    public async Task DetectSheet_DecimalsOnly_ProducesNumeric()
    {
        await WithCsvAsync("price\n10.5\n20.75\n1.25\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Numeric, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            Assert.True(chooser.ColumnTypesBestMatch[0].NumericScale > 0);
            string[] ddl = await ReturnHeadersAsync(import);
            Assert.StartsWith("PRICE NUMERIC(", ddl[0], StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DetectSheet_MixingIntegerAndDecimal_ProducesNumeric()
    {
        // The shared vscode chooser promotes whole-number columns monotonically to
        // NUMERIC when a decimal appears, sizing precision/scale from max digits.
        await WithCsvAsync("val\n10.5\n20\n30.75\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Numeric, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
        });
    }

    [Fact]
    public async Task DetectSheet_TextAndNumbersMix_ProducesText()
    {
        await WithCsvAsync("val\nabc\n123\ndef\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Nvarchar, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            Assert.True(chooser.ColumnTypesBestMatch[0].TextLength >= 3);
        });
    }

    [Fact]
    public async Task DetectSheet_Booleans_AreInferredByTheHost()
    {
        // The host chooser enables boolean inference (inferBoolean) to match the
        // pre-consolidation app behavior; "true"/"false" columns resolve to BOOLEAN.
        await WithCsvAsync("flag\ntrue\nfalse\ntrue\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Boolean, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
        });
    }

    [Fact]
    public async Task DetectSheet_Timestamps_ProducesTimeStamp()
    {
        await WithCsvAsync("ts\n2024-01-15 10:30:00\n2024-02-20 08:05:30\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.TimeStamp, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
        });
    }

    [Fact]
    public async Task DetectSheet_IsoDateInCsv_IsDetectedAsDate()
    {
        // ISO date-only strings map to DATE via the shared chooser.
        await WithCsvAsync("d\n2024-01-15\n2024-02-20\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Date, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
        });
    }

    [Fact]
    public async Task DetectSheet_HeaderTypeSuffix_ForcesDateColumn()
    {
        // "_#DATE" header suffix is the supported way to force DATE for a CSV column.
        await WithCsvAsync("d_#DATE\n2024-01-15\n2024-02-20\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Date, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            string[] ddl = await ReturnHeadersAsync(import);
            Assert.Equal("D__DATE DATE", ddl[0]);
        });
    }

    [Fact]
    public async Task DetectSheet_AllEmptyCsvCells_AreTreatedAsTextNulls()
    {
        // The shared facade skips empty cells and leaves an entirely empty column at
        // the default NVARCHAR, so all values load as NULL.
        await WithCsvAsync("id,note\n1,\n2,\n3,\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Integer, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            Assert.Equal(DbSimpleType.Nvarchar, chooser.ColumnTypesBestMatch[1].DatabaseTypeSimple);
        });
    }

    [Fact]
    public async Task DetectSheet_LongText_GrowsNvarcharLength()
    {
        string longValue = new('x', 400);
        await WithCsvAsync($"text\n{longValue}\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);

            Assert.Equal(DbSimpleType.Nvarchar, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);
            Assert.True(chooser.ColumnTypesBestMatch[0].TextLength >= 400);
        });
    }

    [Fact]
    public async Task UserTypeOverride_ToText_ReflectedInGeneratedDdl()
    {
        await WithCsvAsync("price\n10.5\n20.75\n1.25\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);
            Assert.Equal(DbSimpleType.Numeric, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);

            chooser.ColumnTypesBestMatch[0] = TypeChoice.ToDbTypeWithSize(DbSimpleType.Nvarchar, chooser.ColumnTypesBestMatch[0]);

            string[] ddl = await ReturnHeadersAsync(import);
            Assert.Equal($"PRICE NVARCHAR({DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH})", ddl[0]);
        });
    }

    [Fact]
    public async Task UserTypeOverride_FromTextToInteger_ReflectedInGeneratedDdl()
    {
        await WithCsvAsync("code\n1\n2\n3\n", async import =>
        {
            DatabaseTypeChooser chooser = await RequireDetection(import);
            Assert.Equal(DbSimpleType.Integer, chooser.ColumnTypesBestMatch![0].DatabaseTypeSimple);

            chooser.ColumnTypesBestMatch[0] = TypeChoice.ToDbTypeWithSize(DbSimpleType.Integer, chooser.ColumnTypesBestMatch[0]);

            string[] ddl = await ReturnHeadersAsync(import);
            Assert.Equal("CODE BIGINT", ddl[0]);
        });
    }

    private static async Task WithCsvAsync(string csv, Func<ImportFromExcelFile, Task> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"jbt_detect_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(path, csv);
            var import = new ImportFromExcelFile(null, null) { FilePath = path };
            Assert.True(import.InitImport());
            try
            {
                await action(import);
            }
            finally
            {
                import.DoFileDispose();
            }
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<DatabaseTypeChooser> RequireDetection(ImportFromExcelFile import)
    {
        string sheet = import.SheetNamesToImport![0];
        DatabaseTypeChooser? chooser = await import.DetectSheetAsync(sheet);
        Assert.NotNull(chooser);
        Assert.NotNull(chooser.ColumnTypesBestMatch);
        return chooser;
    }

    private static async Task<string[]> ReturnHeadersAsync(ImportFromExcelFile import)
    {
        string[]? ddl = null;
        await foreach (DbImportJob job in import.ReadFileAndReturnSingleImportJobs())
        {
            ddl = job.ReturnHeadersWithDataTypes(DatabaseTypeEnum.NetezzaSQL);
        }
        Assert.NotNull(ddl);
        return ddl;
    }
}
