using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

public sealed class ResultGridStatsService : IResultGridStatsService
{
    private readonly CellStatsCalculator _cellStatsCalculator = new();
    private DispatcherTimer? _statsTimer;
    private const int StatsUpdateDelayMs = 80;

    public void ScheduleStatsUpdate(Action updateStatsCallback)
    {
        _statsTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(StatsUpdateDelayMs),
            DispatcherPriority.Default,
            (_, _) =>
            {
                _statsTimer?.Stop();
                updateStatsCallback();
            });

        _statsTimer.Stop();
        _statsTimer.Start();
    }

    public CellStatsResult CalculateStats(
        IReadOnlyList<DataGridCellInfo> selectedCells,
        TableOfSqlResults resultsTable)
    {
        if (selectedCells.Count == 0)
        {
            return new CellStatsResult
            {
                SelectedCount = 0,
                Sum = 0,
                NotNullCount = 0,
                DistinctCount = 0,
                Min = null,
                Max = null,
                SelectedValues = []
            };
        }

        var cellValues = new List<(object? Value, TypeCode TypeCode)>(selectedCells.Count);

        foreach (var cellInfo in selectedCells)
        {
            int columnIndex = cellInfo.ColumnIndex;
            int rowIndex = cellInfo.RowIndex;

            if (columnIndex < 0 || rowIndex < 0 || rowIndex >= resultsTable.FilteredRows.Count)
            {
                continue;
            }

            TableRow row = resultsTable.FilteredRows[rowIndex];
            if (columnIndex >= row.Fields.Length)
            {
                continue;
            }

            var value = row.Fields[columnIndex];
            var typeCode = columnIndex >= 0 && columnIndex < resultsTable.TypeCodes.Count
                ? resultsTable.TypeCodes[columnIndex]
                : TypeCode.Object;

            cellValues.Add((value, typeCode));
        }

        return _cellStatsCalculator.Calculate(cellValues);
    }

    public CellStatsResult CalculateStatsFromRawData(
        IReadOnlyList<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)> cellData,
        TableOfSqlResults? resultsTable)
    {
        if (resultsTable is null || cellData.Count == 0)
        {
            return new CellStatsResult
            {
                SelectedCount = 0,
                Sum = 0,
                NotNullCount = 0,
                DistinctCount = 0,
                Min = null,
                Max = null,
                SelectedValues = []
            };
        }

        var cellValues = new List<(object? Value, TypeCode TypeCode)>(cellData.Count);

        foreach (var (columnIndex, rowIndex, value, typeCode) in cellData)
        {
            if (columnIndex < 0 || rowIndex < 0 || rowIndex >= resultsTable.FilteredRows.Count)
            {
                continue;
            }

            TableRow row = resultsTable.FilteredRows[rowIndex];
            if (columnIndex >= row.Fields.Length)
            {
                continue;
            }

            var actualValue = row.Fields[columnIndex];
            var actualTypeCode = columnIndex >= 0 && columnIndex < resultsTable.TypeCodes.Count
                ? resultsTable.TypeCodes[columnIndex]
                : TypeCode.Object;

            cellValues.Add((actualValue, actualTypeCode));
        }

        return _cellStatsCalculator.Calculate(cellValues);
    }
}
