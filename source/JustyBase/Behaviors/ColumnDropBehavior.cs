using Avalonia.Xaml.Interactivity;

namespace JustyBase.Behaviors;

public sealed class ColumnDropBehavior : Behavior<Grid>
{
    public static readonly StyledProperty<string> ColumnNameProperty =
        AvaloniaProperty.Register<ColumnDropBehavior, string>(nameof(ColumnName));

    public static readonly StyledProperty<DataGrid> DataGridProperty =
        AvaloniaProperty.Register<ColumnDropBehavior, DataGrid>(nameof(DataGrid));

    public static readonly StyledProperty<DataFormat<string>> DataFormatProperty =
        AvaloniaProperty.Register<ColumnDropBehavior, DataFormat<string>>(nameof(DataFormat));

    public static readonly StyledProperty<System.Action> RefreshSummaryActionProperty =
        AvaloniaProperty.Register<ColumnDropBehavior, System.Action>(nameof(RefreshSummaryAction));

    public string ColumnName
    {
        get => GetValue(ColumnNameProperty);
        set => SetValue(ColumnNameProperty, value);
    }

    public DataGrid DataGrid
    {
        get => GetValue(DataGridProperty);
        set => SetValue(DataGridProperty, value);
    }

    public DataFormat<string> DataFormat
    {
        get => GetValue(DataFormatProperty);
        set => SetValue(DataFormatProperty, value);
    }

    public System.Action RefreshSummaryAction
    {
        get => GetValue(RefreshSummaryActionProperty);
        set => SetValue(RefreshSummaryActionProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            DragDrop.SetAllowDrop(AssociatedObject, true);
            AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }
        base.OnDetaching();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataFormat != null && e.DataTransfer.Contains(DataFormat))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataGrid == null || DataFormat == null || string.IsNullOrEmpty(ColumnName))
            return;

        if (e.DataTransfer.TryGetValue(DataFormat) is string sourceColName)
        {
            var sourceCol = DataGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == sourceColName);
            var targetCol = DataGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == ColumnName);

            if (sourceCol != null && targetCol != null && sourceCol != targetCol)
            {
                int sourceIndex = sourceCol.DisplayIndex;
                int targetIndex = targetCol.DisplayIndex;

                var pos = e.GetPosition(AssociatedObject);
                bool insertAfter = pos.X > (AssociatedObject.Bounds.Width / 2.0);

                int newDisplayIndex = insertAfter ? targetIndex + 1 : targetIndex;
                if (sourceIndex < newDisplayIndex)
                    newDisplayIndex--;

                newDisplayIndex = Math.Max(0, Math.Min(newDisplayIndex, DataGrid.Columns.Count - 1));
                sourceCol.DisplayIndex = newDisplayIndex;

                RefreshSummaryAction?.Invoke();
            }
            e.Handled = true;
        }
    }
}
