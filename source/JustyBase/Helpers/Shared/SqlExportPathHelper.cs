using JustyBase.Common.Tools;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Helpers.Shared;

/// <summary>
/// Resolves file-type filter, pattern and default extension for SQL export dialogs.
/// Extracted from SqlDocumentViewModel to keep export-path routing testable.
/// </summary>
public static class SqlExportPathHelper
{
    public readonly record struct ExportFileSpec(string FileTypeLabel, string Pattern, string DefaultExtension);

    public static ExportFileSpec ResolveExportSpec(string option)
    {
        if (option.StartsWith(".xlsb", StringComparison.Ordinal))
        {
            return new ExportFileSpec("excel files", "*.xlsb", "xlsb");
        }

        if (option.StartsWith(".parquet", StringComparison.Ordinal))
        {
            return new ExportFileSpec("parquet files", "*.parquet", "parquet");
        }

        var csvEnum = option.GetCsvCompressionEnum();
        return csvEnum switch
        {
            CompressionEnum.None => new ExportFileSpec("csv files", "*.csv", "csv"),
            CompressionEnum.Brotli => new ExportFileSpec("csv files", "*.csv.br", "csv.br"),
            CompressionEnum.Gzip => new ExportFileSpec("csv files", "*.csv.gz", "csv.gz"),
            CompressionEnum.Zstd => new ExportFileSpec("csv files", "*.csv.zst", "csv.zst"),
            CompressionEnum.Zip => new ExportFileSpec("csv files", "*.csv.zip", "csv.zip"),
            _ => throw new System.NotImplementedException()
        };
    }
}
