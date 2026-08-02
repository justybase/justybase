using System.Text;
using System.Globalization;
using Avalonia.Collections;
using JustyBase.Models;
using JustyBase.Models.Tools;

namespace JustyBase.Services.DataGrid;

public sealed class SummaryRowService : ISummaryRowService
{
    public string CalculateSummaryValue(TableOfSqlResults table, int columnIndex, ColumnSummaryType summaryType)
    {
        if (table?.FilteredRows == null || table.FilteredRows.Count == 0)
            return string.Empty;

        var stats = new TableRowStats(table, table.FilteredRows, columnIndex);
        var scale = table.GetNumericScale(columnIndex);
        string format = $"N{(scale <= 0 ? 2 : scale)}";

        return summaryType switch
        {
            ColumnSummaryType.Sum => $"Σ {stats.Sum.ToString(format, CultureInfo.CurrentCulture)}",
            ColumnSummaryType.Count => $"# {stats.NotNullCnt.ToString("N0", CultureInfo.CurrentCulture)}",
            ColumnSummaryType.Average when stats.NotNullCnt > 0 => $"Ø {(stats.Sum / stats.NotNullCnt).ToString(format, CultureInfo.CurrentCulture)}",
            ColumnSummaryType.Min when stats.MinOfColumn.HasValue => $"↓ {stats.MinOfColumn.Value.ToString(format, CultureInfo.CurrentCulture)}",
            ColumnSummaryType.Max when stats.MaxOfColumn.HasValue => $"↑ {stats.MaxOfColumn.Value.ToString(format, CultureInfo.CurrentCulture)}",
            ColumnSummaryType.Distinct => $"≠ {stats.DistinctCnt.ToString("N0", CultureInfo.CurrentCulture)}",
            _ => string.Empty
        };
    }

    public string GetAllStatsTooltip(TableOfSqlResults table, int columnIndex)
    {
        if (table?.FilteredRows == null || table.FilteredRows.Count == 0)
            return string.Empty;

        var stats = new TableRowStats(table, table.FilteredRows, columnIndex);
        var scale = table.GetNumericScale(columnIndex);
        string format = $"N{(scale <= 0 ? 2 : scale)}";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.CurrentCulture, $"Count: {stats.NotNullCnt:N0}");
        sb.AppendLine(CultureInfo.CurrentCulture, $"Distinct: {stats.DistinctCnt:N0}");

        if (stats.Sum != 0)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"Sum: {stats.Sum.ToString(format, CultureInfo.CurrentCulture)}");
            if (stats.NotNullCnt > 0)
            {
                sb.AppendLine(CultureInfo.CurrentCulture, $"Average: {(stats.Sum / stats.NotNullCnt).ToString(format, CultureInfo.CurrentCulture)}");
            }
        }
        if (stats.MinOfColumn.HasValue)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"Min: {stats.MinOfColumn.Value.ToString(format, CultureInfo.CurrentCulture)}");
        }
        if (stats.MaxOfColumn.HasValue)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"Max: {stats.MaxOfColumn.Value.ToString(format, CultureInfo.CurrentCulture)}");
        }

        return sb.ToString().TrimEnd();
    }

    public string CalculateGroupSummaryText(
        TableOfSqlResults table,
        DataGridCollectionViewGroup group,
        Dictionary<int, ColumnSummaryType> columnSummaries)
    {
        if (group?.Items == null || group.Items.Count == 0 || table == null)
            return string.Empty;

        var tableRows = group.Items.OfType<TableRow>().ToList();
        if (tableRows.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (var kvp in columnSummaries.OrderBy(x => x.Key))
        {
            int colIndex = kvp.Key;
            var summaryType = kvp.Value;

            if (summaryType == ColumnSummaryType.None || colIndex >= table.Headers.Count)
                continue;

            string headerName = table.Headers[colIndex];
            var stats = new TableRowStats(table, tableRows, colIndex);
            var scale = table.GetNumericScale(colIndex);
            string format = $"N{(scale <= 0 ? 2 : scale)}";

            string valStr = summaryType switch
            {
                ColumnSummaryType.Sum => $"Σ {stats.Sum.ToString(format, CultureInfo.CurrentCulture)}",
                ColumnSummaryType.Count => $"# {stats.NotNullCnt.ToString("N0", CultureInfo.CurrentCulture)}",
                ColumnSummaryType.Average when stats.NotNullCnt > 0 => $"Ø {(stats.Sum / stats.NotNullCnt).ToString(format, CultureInfo.CurrentCulture)}",
                ColumnSummaryType.Min when stats.MinOfColumn.HasValue => $"↓ {stats.MinOfColumn.Value.ToString(format, CultureInfo.CurrentCulture)}",
                ColumnSummaryType.Max when stats.MaxOfColumn.HasValue => $"↑ {stats.MaxOfColumn.Value.ToString(format, CultureInfo.CurrentCulture)}",
                ColumnSummaryType.Distinct => $"≠ {stats.DistinctCnt.ToString("N0", CultureInfo.CurrentCulture)}",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(valStr))
            {
                if (sb.Length > 0) sb.Append("  |  ");
                sb.Append(CultureInfo.CurrentCulture, $"{headerName}: {valStr}");
            }
        }

        return sb.ToString();
    }
}
