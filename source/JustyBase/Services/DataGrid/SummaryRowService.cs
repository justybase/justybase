using System.Text;
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
            ColumnSummaryType.Sum => $"Σ {stats.Sum.ToString(format)}",
            ColumnSummaryType.Count => $"# {stats.NotNullCnt:N0}",
            ColumnSummaryType.Average when stats.NotNullCnt > 0 => $"Ø {(stats.Sum / stats.NotNullCnt).ToString(format)}",
            ColumnSummaryType.Min when stats.MinOfColumn.HasValue => $"↓ {stats.MinOfColumn.Value.ToString(format)}",
            ColumnSummaryType.Max when stats.MaxOfColumn.HasValue => $"↑ {stats.MaxOfColumn.Value.ToString(format)}",
            ColumnSummaryType.Distinct => $"≠ {stats.DistinctCnt:N0}",
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
        sb.AppendLine($"Count: {stats.NotNullCnt:N0}");
        sb.AppendLine($"Distinct: {stats.DistinctCnt:N0}");

        if (stats.Sum != 0)
        {
            sb.AppendLine($"Sum: {stats.Sum.ToString(format)}");
            if (stats.NotNullCnt > 0)
            {
                sb.AppendLine($"Average: {(stats.Sum / stats.NotNullCnt).ToString(format)}");
            }
        }
        if (stats.MinOfColumn.HasValue)
        {
            sb.AppendLine($"Min: {stats.MinOfColumn.Value.ToString(format)}");
        }
        if (stats.MaxOfColumn.HasValue)
        {
            sb.AppendLine($"Max: {stats.MaxOfColumn.Value.ToString(format)}");
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
                ColumnSummaryType.Sum => $"Σ {stats.Sum.ToString(format)}",
                ColumnSummaryType.Count => $"# {stats.NotNullCnt:N0}",
                ColumnSummaryType.Average when stats.NotNullCnt > 0 => $"Ø {(stats.Sum / stats.NotNullCnt).ToString(format)}",
                ColumnSummaryType.Min when stats.MinOfColumn.HasValue => $"↓ {stats.MinOfColumn.Value.ToString(format)}",
                ColumnSummaryType.Max when stats.MaxOfColumn.HasValue => $"↑ {stats.MaxOfColumn.Value.ToString(format)}",
                ColumnSummaryType.Distinct => $"≠ {stats.DistinctCnt:N0}",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(valStr))
            {
                if (sb.Length > 0) sb.Append("  |  ");
                sb.Append($"{headerName}: {valStr}");
            }
        }

        return sb.ToString();
    }
}
