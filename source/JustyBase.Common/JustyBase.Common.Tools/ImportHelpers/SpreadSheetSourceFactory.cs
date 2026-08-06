using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using SpreadSheetTasks;
using System.Text;

namespace JustyBase.Common.Tools.ImportHelpers;

/// <summary>
/// Host <see cref="IImportSourceFactory"/>: CSV goes through the shared reader adapter and
/// Excel formats through the native SpreadSheetTasks reader (xlsb is exclusive-open).
/// </summary>
public sealed class SpreadSheetSourceFactory : IImportSourceFactory
{
    public IImportSource OpenSource(string filePath, Encoding? encoding)
    {
        ArgumentNullException.ThrowIfNull(filePath);

#pragma warning disable CA2000 // ownership transferred to the returned IImportSource wrapper
        if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase))
        {
            var excel = new XlsxOrXlsbReadOrEdit();
            excel.Open(filePath, true, encoding: encoding);
            return new SpreadSheetImportSource(excel, isExclusiveOpen: filePath.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase))
            {
                FilePath = filePath
            };
        }

        CompressionEnum compression = filePath.GetCsvCompressionEnum();
        var csv = compression == CompressionEnum.None ? new CsvReader() : new CsvReader(compression);
        csv.Open(filePath, true, encoding: encoding);
        return new SpreadSheetImportSource(csv, isExclusiveOpen: false)
        {
            FilePath = filePath
        };
#pragma warning restore CA2000
    }

    public bool IsExclusiveOpen(string filePath)
        => filePath.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase);
}
