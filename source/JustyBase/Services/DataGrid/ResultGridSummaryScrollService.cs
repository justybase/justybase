using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using DataGridControl = Avalonia.Controls.DataGrid;

namespace JustyBase.Services.DataGrid;

public sealed class ResultGridSummaryScrollService : IResultGridSummaryScrollService
{
    private const double DefaultRowHeaderWidth = 45;

    private double? _cachedRowHeaderWidth;

    public Vector SyncHorizontalOffset(Vector currentOffset, double newOffset)
    {
        return currentOffset.WithX(newOffset);
    }

    public double ResolveFirstColumnSpacerWidth(double fallbackRowHeaderWidth, double? translatedColumnX, double scrollOffsetX)
    {
        if (translatedColumnX is null)
        {
            return Math.Max(0, fallbackRowHeaderWidth);
        }

        double absoluteX = translatedColumnX.Value + scrollOffsetX;
        return Math.Max(0, absoluteX);
    }

    public void InvalidateRowHeaderWidthCache()
    {
        _cachedRowHeaderWidth = null;
    }

    public double GetFirstColumnSpacerWidth(DataGridControl dataGrid, ScrollViewer? summaryScrollViewer)
    {
        double rowHeaderWidth = MeasureRowHeaderWidth(dataGrid);

        var firstColumn = dataGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .FirstOrDefault(c => c.IsVisible);
        if (firstColumn is null)
        {
            return rowHeaderWidth;
        }

        var headersPresenter = dataGrid.GetVisualDescendants()
            .OfType<DataGridColumnHeadersPresenter>()
            .FirstOrDefault();
        if (headersPresenter is null)
        {
            return rowHeaderWidth;
        }

        var columnHeader = headersPresenter.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, firstColumn.Header));

        double? translatedColumnX = columnHeader?.TranslatePoint(new Point(0, 0), dataGrid)?.X;
        double scrollOffsetX = summaryScrollViewer?.Offset.X ?? 0;
        return ResolveFirstColumnSpacerWidth(rowHeaderWidth, translatedColumnX, scrollOffsetX);
    }

    private double MeasureRowHeaderWidth(DataGridControl dataGrid)
    {
        if (_cachedRowHeaderWidth is double cached)
        {
            return cached;
        }

        var rowHeader = dataGrid.GetVisualDescendants()
            .OfType<DataGridRowHeader>()
            .FirstOrDefault();

        _cachedRowHeaderWidth = rowHeader?.Bounds.Width ?? DefaultRowHeaderWidth;
        return _cachedRowHeaderWidth.Value;
    }
}
