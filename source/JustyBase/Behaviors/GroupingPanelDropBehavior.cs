using Avalonia.Xaml.Interactivity;

namespace JustyBase.Behaviors;

/// <summary>
/// Xaml.Behaviors-based behavior for the grouping panel drop zone.
/// Handles DragOver, DragLeave, and Drop events for column grouping.
/// Replaces manual event handler wiring in SqlResultsView constructor.
/// </summary>
public sealed class GroupingPanelDropBehavior : Behavior<Border>
{
    public static readonly StyledProperty<DataFormat<string>> ColumnNameDataFormatProperty =
        AvaloniaProperty.Register<GroupingPanelDropBehavior, DataFormat<string>>(nameof(ColumnNameDataFormat));

    public static readonly StyledProperty<Action<string>> GroupByColumnActionProperty =
        AvaloniaProperty.Register<GroupingPanelDropBehavior, Action<string>>(nameof(GroupByColumnAction));

    public DataFormat<string> ColumnNameDataFormat
    {
        get => GetValue(ColumnNameDataFormatProperty);
        set => SetValue(ColumnNameDataFormatProperty, value);
    }

    /// <summary>
    /// Action to invoke when a column is dropped, passing the column name.
    /// </summary>
    public Action<string> GroupByColumnAction
    {
        get => GetValue(GroupByColumnActionProperty);
        set => SetValue(GroupByColumnActionProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }
        base.OnDetaching();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (AssociatedObject == null || ColumnNameDataFormat == null)
            return;

        if (e.DataTransfer.Contains(ColumnNameDataFormat))
        {
            e.DragEffects = DragDropEffects.Link;
            e.Handled = true;
            if (!AssociatedObject.Classes.Contains("dragOver"))
            {
                AssociatedObject.Classes.Add("dragOver");
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            AssociatedObject.Classes.Remove("dragOver");
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        AssociatedObject?.Classes.Remove("dragOver");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        AssociatedObject?.Classes.Remove("dragOver");

        if (ColumnNameDataFormat == null || GroupByColumnAction == null)
            return;

        if (e.DataTransfer.TryGetValue(ColumnNameDataFormat) is string columnName && !string.IsNullOrEmpty(columnName))
        {
            GroupByColumnAction(columnName);
            e.Handled = true;
        }
    }
}
