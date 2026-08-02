using JustyBase.Models;
using System.Collections;

namespace JustyBase.Services.DataGrid;

public interface IDataGridClipboardService
{
    Task<string> BuildAllRowsTextAsync(TableOfSqlResults table, IReadOnlyList<string> columnHeaders);

    string BuildMultiRowText(IReadOnlyList<string> columnHeaders, IList selectedItems);

    string BuildSingleCellText(TableRow row, string columnHeader, TableOfSqlResults table);
}
