using Avalonia.Collections;
using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

public interface ISummaryRowService
{
    string CalculateSummaryValue(TableOfSqlResults table, int columnIndex, ColumnSummaryType summaryType);
    string GetAllStatsTooltip(TableOfSqlResults table, int columnIndex);
    string CalculateGroupSummaryText(
        TableOfSqlResults table,
        DataGridCollectionViewGroup group,
        Dictionary<int, ColumnSummaryType> columnSummaries);
}
