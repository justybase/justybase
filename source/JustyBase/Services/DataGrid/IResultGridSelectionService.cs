namespace JustyBase.Services.DataGrid;

public readonly record struct ResultGridSelectionChangePlan(
    string StatusMessage,
    bool IsSingleCellSelection,
    bool ShouldRefreshRowDetails,
    int UpdatedPreviousSelectedCount);

public interface IResultGridSelectionService
{
    ResultGridSelectionChangePlan BuildSelectionChangePlan(
        int selectedCellsCount,
        int selectedRowsCount,
        int previousSelectedCount);

    int GetRowDetailValueColumnCount(int selectedRowsCount, int maxColumns = 10);
}
