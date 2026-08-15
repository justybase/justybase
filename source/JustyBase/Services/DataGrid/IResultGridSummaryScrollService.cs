using Avalonia.Controls;
using DataGridControl = Avalonia.Controls.DataGrid;

namespace JustyBase.Services.DataGrid;

public interface IResultGridSummaryScrollService
{
    Vector SyncHorizontalOffset(Vector currentOffset, double newOffset);

    double ResolveFirstColumnSpacerWidth(double fallbackRowHeaderWidth, double? translatedColumnX, double scrollOffsetX);

    /// <summary>
    /// Resolves the spacer width that aligns the first summary cell with the first
    /// visible grid column, accounting for row headers, grouping indentation and
    /// horizontal scroll offset.
    /// </summary>
    double GetFirstColumnSpacerWidth(DataGridControl dataGrid, ScrollViewer? summaryScrollViewer);

    /// <summary>
    /// Drops the cached row-header measurement. Call when grid columns are recreated,
    /// data is replaced or the frozen-column count changes.
    /// </summary>
    void InvalidateRowHeaderWidthCache();
}
