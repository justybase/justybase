using System.Data;
using System.Text;
using JustyBase.Common.Tools;
using JustyBase.ImportExport.Export;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services;

namespace JustyBase.Tests;

/// <summary>Smoke tests that Avalonia host adapters stay wired to shared Core/ImportExport.</summary>
public sealed class SharedCoreHostAdapterTests
{
    [Fact]
    public void SqlRiskAnalysis_maps_core_risks_to_lint_codes()
    {
        var issues = SqlRiskAnalysisService.Analyze("""
            UPDATE t SET a = 1;
            SELECT * INTO bak FROM t;
            CREATE TABLE x (id INT);
            """, "NetezzaSQL");

        Assert.Contains(issues, i => i.RuleId == "RISK001");
        Assert.Contains(issues, i => i.RuleId == "RISK003");
        Assert.Contains(issues, i => i.RuleId == "RISK002");
    }

    [Fact]
    public void SqlRiskAnalysis_leading_whitespace_still_detects_unsafe_update()
    {
        var issues = SqlRiskAnalysisService.Analyze("  UPDATE t SET a = 1");
        Assert.Contains(issues, i => i.RuleId == "RISK001");
    }

    [Fact]
    public void SasMacroPreprocessor_expands_let_and_ampersand_variables()
    {
        SasMacroPreprocessor.ClearSessionMacros();
        string expanded = SasMacroPreprocessor.Expand("%let name = Ada;\nSELECT &name;");
        Assert.Contains("SELECT Ada", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("%let", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Uncompressed_csv_export_uses_shared_CsvExportWriter_path()
    {
        using var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));
        table.Rows.Add(1, "a|b");
        table.Rows.Add(2, "plain");

        string path = Path.Combine(Path.GetTempPath(), $"jb-csv-{Guid.NewGuid():N}.csv");
        try
        {
            using var reader = table.CreateDataReader();
            string written = await ExportDbReaderExtensions.HandleCsvOrParquetOutput(
                reader,
                path,
                new AdvancedExportOptions
                {
                    Delimiter = '|',
                    LineDelimiter = "\n",
                    Header = true,
                    Encod = new UTF8Encoding(false),
                    CompresionType = CompressionEnum.None
                },
                progressAction: null);

            Assert.Equal(path, written);
            string text = File.ReadAllText(path);
            Assert.Contains("id|name", text, StringComparison.Ordinal);
            Assert.Contains(CsvExportWriter.Escape("a|b", '|'), text, StringComparison.Ordinal);
            Assert.Contains("1|\"a|b\"", text, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
