using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace JustyBase.Behaviors;

/// <summary>
/// Xaml.Behaviors-based behavior for reordering grouped column tags via drag-and-drop.
/// Replaces manual PointerPressed/Moved/Released and DragOver/Drop event handler wiring.
/// Attach to each grouped column Border in the grouping panel.
/// </summary>
public sealed class GroupedColumnReorderBehavior : Behavior<Border>
{
    public static readonly StyledProperty<string> ReorderDataFormatProperty =
        AvaloniaProperty.Register<GroupedColumnReorderBehavior, string>(nameof(ReorderDataFormat), "ReorderColumnName");

    public static readonly StyledProperty<Action<string, string>> MoveGroupActionProperty =
        AvaloniaProperty.Register<GroupedColumnReorderBehavior, Action<string, string>>(nameof(MoveGroupAction));

    public string ReorderDataFormat
    {
        get => GetValue(ReorderDataFormatProperty);
        set => SetValue(ReorderDataFormatProperty, value);
    }

    /// <summary>
    /// Action to invoke when a group is moved: (sourceColumnName, targetColumnName).
    /// </summary>
    public Action<string, string> MoveGroupAction
    {
        get => GetValue(MoveGroupActionProperty);
        set => SetValue(MoveGroupActionProperty, value);
    }

    private Point? _dragStartPoint;
    private PointerPressedEventArgs? _dragStartEventArgs;
    private const double DragThreshold = 5.0;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }
        base.OnDetaching();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject != null && e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(AssociatedObject);
            _dragStartEventArgs = e;
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragStartPoint.HasValue || AssociatedObject == null || ReorderDataFormat == null)
            return;

        if (!e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
            return;

        var currentPos = e.GetPosition(AssociatedObject);
        var distance = Math.Sqrt(
            Math.Pow(currentPos.X - _dragStartPoint.Value.X, 2) +
            Math.Pow(currentPos.Y - _dragStartPoint.Value.Y, 2));

        if (distance >= DragThreshold)
        {
            var dragStartEventArgs = _dragStartEventArgs;
            _dragStartPoint = null;
            _dragStartEventArgs = null;
            if (AssociatedObject.DataContext is string columnName)
            {
                using var dragData = new DataTransfer();
                var reorderDataFormat = DataFormat.CreateStringApplicationFormat(ReorderDataFormat);
                dragData.Add(DataTransferItem.Create(reorderDataFormat, columnName));

                if (!AssociatedObject.Classes.Contains("dragging"))
                    AssociatedObject.Classes.Add("dragging");

                try
                {
                    if (dragStartEventArgs is not null)
                    {
                        await DragDrop.DoDragDropAsync(dragStartEventArgs, dragData, DragDropEffects.Move);
                    }
                }
                finally
                {
                    AssociatedObject?.Classes.Remove("dragging");
                }
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStartPoint = null;
        _dragStartEventArgs = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (ReorderDataFormat != null && e.DataTransfer.Contains(DataFormat.CreateStringApplicationFormat(ReorderDataFormat))
            && AssociatedObject?.DataContext is string)
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ReorderDataFormat == null || MoveGroupAction == null || AssociatedObject == null)
            return;

        if (e.DataTransfer.TryGetValue(DataFormat.CreateStringApplicationFormat(ReorderDataFormat)) is string sourceCol
            && AssociatedObject.DataContext is string targetCol)
        {
            if (sourceCol != targetCol)
            {
                MoveGroupAction(sourceCol, targetCol);
            }
            e.Handled = true;
        }
    }
}
