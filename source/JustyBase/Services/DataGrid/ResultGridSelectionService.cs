namespace JustyBase.Services.DataGrid;

public sealed class ResultGridSelectionService : IResultGridSelectionService
{
    public ResultGridSelectionChangePlan BuildSelectionChangePlan(
        int selectedCellsCount,
        int selectedRowsCount,
        int previousSelectedCount)
    {
        if (selectedCellsCount > 0)
        {
            return new ResultGridSelectionChangePlan(
                $"Selected {selectedCellsCount:N0} cells",
                IsSingleCellSelection: selectedCellsCount == 1,
                ShouldRefreshRowDetails: false,
                UpdatedPreviousSelectedCount: previousSelectedCount);
        }

        bool shouldRefresh = previousSelectedCount != selectedRowsCount;
        int updatedPreviousSelectedCount = shouldRefresh ? selectedRowsCount : previousSelectedCount;
        return new ResultGridSelectionChangePlan(
            $"Selected {selectedRowsCount:N0} rows",
            IsSingleCellSelection: false,
            ShouldRefreshRowDetails: shouldRefresh,
            UpdatedPreviousSelectedCount: updatedPreviousSelectedCount);
    }

    public int GetRowDetailValueColumnCount(int selectedRowsCount, int maxColumns = 10)
    {
        int normalizedSelectedRows = Math.Max(0, selectedRowsCount);
        int normalizedMaxColumns = Math.Max(0, maxColumns);
        return Math.Min(normalizedSelectedRows, normalizedMaxColumns);
    }
}
