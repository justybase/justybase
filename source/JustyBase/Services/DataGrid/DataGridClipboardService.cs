using System.Collections;
using System.Text;
using JustyBase.Models;
using JustyBase.PluginCommons;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Handles clipboard copy operations for DataGrid results.
/// Extracted from SqlResultsView to separate clipboard logic from view code.
/// </summary>
public sealed class DataGridClipboardService : IDataGridClipboardService
{
    /// <summary>
    /// Builds a tab-delimited string of all rows with headers for clipboard.
    /// </summary>
    public async Task<string> BuildAllRowsTextAsync(TableOfSqlResults table, IReadOnlyList<string> columnHeaders)
    {
        if (table?.FilteredRows is null || table.FilteredRows.Count == 0)
            return string.Empty;

        var rows = table.FilteredRows;
        string result = string.Empty;

        await Task.Run(() =>
        {
            var sb = new StringBuilder();

            for (int i = 0; i < columnHeaders.Count; i++)
            {
                sb.Append(columnHeaders[i]);
                if (i < columnHeaders.Count - 1)
                {
                    sb.Append('\t');
                }
            }
            sb.AppendLine();

            foreach (var row in rows)
            {
                for (int i = 0; i < row.Fields.Length; i++)
                {
                    var val = row.Fields[i];
                    if (val is null || val == DBNull.Value)
                    {
                        sb.Append("");
                    }
                    else
                    {
                        sb.Append(val.ToString()?.Replace("\t", " ").Replace("\n", " ").Replace("\r", ""));
                    }
                    if (i < row.Fields.Length - 1)
                    {
                        sb.Append('\t');
                    }
                }
                sb.AppendLine();
            }

            result = sb.ToString();
        });

        return result;
    }

    /// <summary>
    /// Builds a clipboard string for multi-row selection (with headers).
    /// </summary>
    public string BuildMultiRowText(IReadOnlyList<string> columnHeaders, IList selectedItems)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < columnHeaders.Count; i++)
        {
            sb.Append(columnHeaders[i]);
            if (i < columnHeaders.Count - 1)
            {
                sb.Append('\t');
            }
        }

        sb.AppendLine();
        for (int index = 0; index < selectedItems.Count; index++)
        {
            if (selectedItems[index] is TableRow tableRow)
            {
                sb.AppendLine(String.Join('\t', tableRow.Fields));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a clipboard string for single-cell selection.
    /// </summary>
    public string BuildSingleCellText(TableRow row, string columnHeader, TableOfSqlResults table)
    {
        int ind = table.Headers.IndexOf(columnHeader);
        if (ind < 0 || ind >= row.Fields.Length)
            return string.Empty;

        var obj = row.Fields[ind];
        if (obj is string objStr)
        {
            return objStr;
        }
        else
        {
            return StringExtension.ConvertAsSqlCompatybile(obj);
        }
    }

    /// <inheritdoc />
    public string BuildCopyWithHeadersText(
        IReadOnlyList<string> columnHeaders,
        IList selectedItems,
        object? selectedItem,
        string? currentColumnHeader,
        TableOfSqlResults? table)
    {
        if (selectedItems.Count > 1)
        {
            return BuildMultiRowText(columnHeaders, selectedItems);
        }

        if (selectedItem is TableRow tableRow && currentColumnHeader is not null && table is not null)
        {
            return BuildSingleCellText(tableRow, currentColumnHeader, table);
        }

        return string.Empty;
    }

    /// <inheritdoc />
    public string BuildSelectedCellsColumnText(IEnumerable<object?>? cells)
    {
        if (cells is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var cell in cells)
        {
            sb.AppendLine(cell?.ToString());
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string BuildSelectedRangeText(
        IReadOnlyList<string> columnHeaders,
        IEnumerable<TableRow> rows,
        int fromColumn,
        int toColumn)
    {
        if (columnHeaders is null || columnHeaders.Count == 0 || rows is null)
        {
            return string.Empty;
        }

        int first = Math.Min(fromColumn, toColumn);
        int last = Math.Max(fromColumn, toColumn);

        // PrevCols may hold stale indexes after reorder/hide/remove columns or a new query.
        if (last < 0 || first >= columnHeaders.Count)
        {
            return string.Empty;
        }

        first = Math.Max(first, 0);
        last = Math.Min(last, columnHeaders.Count - 1);
        if (first > last)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        for (int i = first; i <= last; i++)
        {
            sb.Append(columnHeaders[i]);
            // Header cells are all followed by a tab, including the last one.
            sb.Append('\t');
        }
        sb.AppendLine();

        foreach (var row in rows)
        {
            if (row?.Fields is not { } fields)
            {
                continue;
            }
            for (int i = first; i <= last; i++)
            {
                if (i < fields.Length)
                {
                    sb.Append(fields[i]);
                }
                if (i < last)
                {
                    sb.Append('\t');
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string BuildRowValuesText(TableRow row)
    {
        object[] fields = row.Fields;

        var sb = new StringBuilder();
        sb.Append("VALUES (");
        for (int i = 0; i < fields.Length; i++)
        {
            sb.Append(StringExtension.ConvertAsSqlCompatybile(fields[i]));
            if (i < fields.Length - 1)
            {
                sb.Append(',');
            }
        }
        sb.Append(')');

        return sb.ToString();
    }
}
