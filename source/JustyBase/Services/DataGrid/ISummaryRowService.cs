using Avalonia.Collections;
using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

public interface ISummaryRowService
{
    string CalculateSummaryValue(TableOfSqlResults table, int columnIndex, ColumnSummaryType summaryType);

    /// <summary>
    /// Calculates a summary over an explicit row subset (e.g. the currently
    /// filtered collection view) instead of <see cref="TableOfSqlResults.FilteredRows"/>.
    /// </summary>
    string CalculateSummaryValue(TableOfSqlResults table, IReadOnlyList<TableRow> rows, int columnIndex, ColumnSummaryType summaryType);

    string GetAllStatsTooltip(TableOfSqlResults table, int columnIndex);

    /// <summary>
    /// Builds a stats tooltip over an explicit row subset.
    /// </summary>
    string GetAllStatsTooltip(TableOfSqlResults table, IReadOnlyList<TableRow> rows, int columnIndex);

    string CalculateGroupSummaryText(
        TableOfSqlResults table,
        DataGridCollectionViewGroup group,
        Dictionary<int, ColumnSummaryType> columnSummaries);
}
