using JustyBase.Helpers.Shared;

namespace JustyBase.Tests;

public class SqlExportPathHelperTests
{
    [Theory]
    [InlineData(".xlsb", "excel files", "*.xlsb", "xlsb")]
    [InlineData(".xlsb (compressed)", "excel files", "*.xlsb", "xlsb")]
    [InlineData(".parquet", "parquet files", "*.parquet", "parquet")]
    [InlineData(".parquet (columnar)", "parquet files", "*.parquet", "parquet")]
    public void ResolveExportSpec_NonCsvFormats(string option, string expectedLabel, string expectedPattern, string expectedExt)
    {
        var spec = SqlExportPathHelper.ResolveExportSpec(option);
        Assert.Equal(expectedLabel, spec.FileTypeLabel);
        Assert.Equal(expectedPattern, spec.Pattern);
        Assert.Equal(expectedExt, spec.DefaultExtension);
    }

    [Theory]
    [InlineData(".csv", "csv files", "*.csv", "csv")]
    [InlineData(".csv.br", "csv files", "*.csv.br", "csv.br")]
    [InlineData(".csv.gz", "csv files", "*.csv.gz", "csv.gz")]
    [InlineData(".csv.zst", "csv files", "*.csv.zst", "csv.zst")]
    [InlineData(".csv.zip", "csv files", "*.csv.zip", "csv.zip")]
    public void ResolveExportSpec_CsvFormats(string option, string expectedLabel, string expectedPattern, string expectedExt)
    {
        var spec = SqlExportPathHelper.ResolveExportSpec(option);
        Assert.Equal(expectedLabel, spec.FileTypeLabel);
        Assert.Equal(expectedPattern, spec.Pattern);
        Assert.Equal(expectedExt, spec.DefaultExtension);
    }
}
