namespace JustyBase.Services.DataGrid;

public interface IResultGridSummaryRefreshService
{
    bool HaveColumnWidthsChanged(
        IReadOnlyList<double> currentColumnWidths,
        Dictionary<int, double> lastColumnWidths,
        double tolerance = 0.5);

    bool ShouldRefreshSummaryRow(bool showSummaryRow, int columnCount);

    bool ShouldRefreshGroupHeaderSummaries(int summaryCount, int groupCount);
}
