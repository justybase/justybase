using JustyBase.Models;
using System.Collections;

namespace JustyBase.Services.DataGrid;

public interface IDataGridClipboardService
{
    Task<string> BuildAllRowsTextAsync(TableOfSqlResults table, IReadOnlyList<string> columnHeaders);

    string BuildMultiRowText(IReadOnlyList<string> columnHeaders, IList selectedItems);

    string BuildSingleCellText(TableRow row, string columnHeader, TableOfSqlResults table);

    /// <summary>
    /// Builds the clipboard text for the current selection, preferring multi-row output when
    /// several rows are selected and falling back to the current cell otherwise.
    /// </summary>
    string BuildCopyWithHeadersText(
        IReadOnlyList<string> columnHeaders,
        IList selectedItems,
        object? selectedItem,
        string? currentColumnHeader,
        TableOfSqlResults? table);

    /// <summary>
    /// One value per line for the "copy selected cells of current column" toolbar action.
    /// A null cell list yields an empty string instead of throwing.
    /// </summary>
    string BuildSelectedCellsColumnText(IEnumerable<object?>? cells);

    /// <summary>
    /// Tab-delimited header row plus data rows for a column range. The header line keeps
    /// the trailing tab produced by the original implementation.
    /// </summary>
    string BuildSelectedRangeText(
        IReadOnlyList<string> columnHeaders,
        IEnumerable<TableRow> rows,
        int fromColumn,
        int toColumn);

    /// <summary>SQL <c>VALUES (a,b,c)</c> literal for a single row (CSV-escaped values).</summary>
    string BuildRowValuesText(TableRow row);
}
