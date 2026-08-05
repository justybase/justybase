using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

public interface IResultGridStatsService
{
    void ScheduleStatsUpdate(Action updateStatsCallback);
    CellStatsResult CalculateStats(
        IReadOnlyList<DataGridCellInfo> selectedCells,
        TableOfSqlResults resultsTable);
}
