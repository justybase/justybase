using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.Helpers;

namespace JustyBase.Tests;

public sealed class TabularPasteDetectorTests
{
    [Fact]
    public void Detect_ExcelStyleBlockWithConsistentTabs_ReturnsDimensions()
    {
        string text = string.Join("\r\n", "col1\tcol2", "0\t0", "1\t0", "2\t0");

        TabularPasteInfo? info = TabularPasteDetector.Detect(text);

        Assert.Equal(new TabularPasteInfo(Lines: 4, Columns: 2), info);
        Assert.Equal(
            "4 tab-separated lines with 2 columns",
            TabularPasteDetector.Describe(info!.Value));
    }

    [Fact]
    public void Detect_IgnoresTrailingTabsAndBlankLines()
    {
        TabularPasteInfo? info = TabularPasteDetector.Detect("A\tB\t\n1\t2\t\n3\t4\t\n\n");

        Assert.Equal(new TabularPasteInfo(Lines: 3, Columns: 2), info);
    }

    [Fact]
    public void Detect_RejectsSqlScriptsAndRaggedIndentation()
    {
        Assert.Null(TabularPasteDetector.Detect("SELECT 1\nFROM ADMIN.T\nWHERE ID = 1"));
        Assert.Null(TabularPasteDetector.Detect("SELECT\t1\nFROM\tADMIN.T\nWHERE\tID = 1"));
        Assert.Null(TabularPasteDetector.Detect("A\tB\n1\t2\t3\n4\t5"));
    }

    [Fact]
    public void Detect_RejectsShortOrSingleColumnBlocks()
    {
        Assert.Null(TabularPasteDetector.Detect("A\tB\n1\t2"));
        Assert.Null(TabularPasteDetector.Detect("A\nB\nC"));
        Assert.Null(TabularPasteDetector.Detect("A\tB\n\n1\t2"));
        Assert.Null(TabularPasteDetector.Detect(""));
        Assert.Null(TabularPasteDetector.Detect(null));
    }

    [Fact]
    public void ToCsv_QuotesCommasQuotesAndSkipsTrailingEmptyFields()
    {
        string csv = TabularPasteDetector.ToCsv("name\tnote\t\nAlpha\tx,y\t\n\"Beta\"\t\"say \"\"hi\"\"\"\t\n");

        Assert.Equal("name,note\nAlpha,\"x,y\"\nBeta,\"say \"\"hi\"\"\"", csv);
    }

    [Fact]
    public void ToCsv_PadsRaggedRowsToTheWidestRow()
    {
        string csv = TabularPasteDetector.ToCsv("A\tB\n1\n2");

        Assert.Equal("A,B\n1,\n2,", csv);
    }

    [Fact]
    public async Task ToCsv_RoundTripsThroughTheImportScanner()
    {
        string csv = TabularPasteDetector.ToCsv("name\tnote\nAlpha\tx,y\nBeta\t\"say \"\"hi\"\"\"");
        string path = Path.Combine(Path.GetTempPath(), $"justybase-paste-test-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv);

        try
        {
            using var importFile = new ImportFromExcelFile(static _ => { }, logger: null)
            {
                FilePath = path
            };

            Assert.True(importFile.InitImport());
            string sheet = Assert.Single(importFile.SheetNamesToImport);

            DatabaseTypeChooser? chooser = await importFile.DetectSheetAsync(sheet);

            Assert.NotNull(chooser);
            Assert.Equal(["NAME", "NOTE"], chooser!.NormalizedColumnHeaderNames);
            Assert.Equal(2, chooser.PreviewRows.Count);
            Assert.Equal("x,y", chooser.PreviewRows[0][1]);
            Assert.Equal("say \"hi\"", chooser.PreviewRows[1][1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
