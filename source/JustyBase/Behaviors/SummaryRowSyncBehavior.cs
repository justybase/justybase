using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using JustyBase.Services.DataGrid;

namespace JustyBase.Behaviors;

/// <summary>
/// Behavior that synchronizes the horizontal scrolling of a DataGrid with a summary ScrollViewer.
/// Attach to the DataGrid.
/// </summary>
public sealed class SummaryRowSyncBehavior : Behavior<DataGrid>
{
    public static readonly StyledProperty<ScrollViewer> SummaryScrollViewerProperty =
        AvaloniaProperty.Register<SummaryRowSyncBehavior, ScrollViewer>(nameof(SummaryScrollViewer));

    public static readonly StyledProperty<object> RecalculateActionProperty =
        AvaloniaProperty.Register<SummaryRowSyncBehavior, object>(nameof(RecalculateAction));

    public ScrollViewer SummaryScrollViewer
    {
        get => GetValue(SummaryScrollViewerProperty);
        set => SetValue(SummaryScrollViewerProperty, value);
    }

    /// <summary>
    /// Needs to be Action (bound as delegate) since Behavior doesn't support commanding well for internal calls
    /// </summary>
    public object RecalculateAction
    {
        get => GetValue(RecalculateActionProperty);
        set => SetValue(RecalculateActionProperty, value);
    }

    private ScrollViewer? _gridScrollViewer;
    private ScrollBar? _gridHorizontalScrollBar;
    private IResultGridSummaryScrollService? _scrollService;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            // Use LayoutUpdated for scroll sync as HorizontalScroll event doesn't exist in Avalonia DataGrid
            AssociatedObject.ColumnReordered += OnColumnReordered;
            AssociatedObject.PropertyChanged += OnPropertyChanged;
            AssociatedObject.LayoutUpdated += OnLayoutUpdated;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            // Use LayoutUpdated for scroll sync as HorizontalScroll event doesn't exist in Avalonia DataGrid
            AssociatedObject.ColumnReordered -= OnColumnReordered;
            AssociatedObject.PropertyChanged -= OnPropertyChanged;
            AssociatedObject.LayoutUpdated -= OnLayoutUpdated;
        }
        base.OnDetaching();
    }

    public void Initialize(IResultGridSummaryScrollService scrollService)
    {
         _scrollService = scrollService;
    }

    private void OnHorizontalScroll(object? sender, DataGridScrollEventArgs e)
    {
        SyncSummaryRowScroll(e.NewValue);
    }

    private void OnColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        InvalidateSummaryLayout();
        RefreshSummaryRowWidths();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DataGrid.FrozenColumnCountProperty)
        {
            InvalidateSummaryLayout();
            RefreshSummaryRowWidths();
        }
    }

    private void InvalidateSummaryLayout()
    {
        if (_scrollService is not null)
        {
            _scrollService.InvalidateRowHeaderWidthCache();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        SyncSummaryRowScroll(GetCurrentGridHorizontalOffset());
    }

    private void SyncSummaryRowScroll(double newOffset)
    {
        if (SummaryScrollViewer != null && _scrollService != null)
        {
            SummaryScrollViewer.Offset = _scrollService.SyncHorizontalOffset(SummaryScrollViewer.Offset, newOffset);
        }
    }

    private double GetCurrentGridHorizontalOffset()
    {
        if (_gridScrollViewer == null && AssociatedObject != null)
        {
            _gridScrollViewer = AssociatedObject.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
        }

        if (_gridScrollViewer != null)
        {
            return _gridScrollViewer.Offset.X;
        }

        if (_gridHorizontalScrollBar == null && AssociatedObject != null)
        {
            _gridHorizontalScrollBar = AssociatedObject.GetVisualDescendants()
                .OfType<ScrollBar>()
                .FirstOrDefault(sb => sb.Orientation == Avalonia.Layout.Orientation.Horizontal);
        }

        return _gridHorizontalScrollBar?.Value ?? 0;
    }

    private void RefreshSummaryRowWidths()
    {
        if (RecalculateAction is System.Action action)
        {
            action();
        }
    }
}
