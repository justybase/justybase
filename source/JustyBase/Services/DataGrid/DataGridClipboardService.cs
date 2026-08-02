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
}
