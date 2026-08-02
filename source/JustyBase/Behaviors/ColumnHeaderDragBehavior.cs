using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace JustyBase.Behaviors;

public sealed class ColumnHeaderDragBehavior : Behavior<TextBlock>
{
    public static readonly StyledProperty<string> ColumnNameProperty =
        AvaloniaProperty.Register<ColumnHeaderDragBehavior, string>(nameof(ColumnName));

    public static readonly StyledProperty<DataFormat<string>> DataFormatProperty =
        AvaloniaProperty.Register<ColumnHeaderDragBehavior, DataFormat<string>>(nameof(DataFormat));

    public string ColumnName
    {
        get => GetValue(ColumnNameProperty);
        set => SetValue(ColumnNameProperty, value);
    }

    public DataFormat<string> DataFormat
    {
        get => GetValue(DataFormatProperty);
        set => SetValue(DataFormatProperty, value);
    }

    private Point? _dragStartPoint;
    private PointerPressedEventArgs? _dragStartEventArgs;
    private const double DragThreshold = 5.0;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.Cursor = new Cursor(StandardCursorType.Hand);
            // DataGrid headers may handle pointer events themselves. Listen even when
            // the event was already handled so the drag gesture remains available.
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        }
        base.OnDetaching();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(AssociatedObject);
            _dragStartEventArgs = e;
            e.Pointer.Capture(AssociatedObject);
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragStartPoint.HasValue || !e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
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

            if (string.IsNullOrEmpty(ColumnName) || DataFormat == null || dragStartEventArgs is null)
            {
                e.Pointer.Capture(null);
                return;
            }

            using var dragData = new DataTransfer();
            dragData.Add(DataTransferItem.Create(DataFormat, ColumnName));
            // Avalonia 12 requires the original press event here (Wayland/XDND use
            // the implicit grab created by that event). Do not release the capture
            // before Avalonia has started the drag; DoDragDropAsync owns it then.
            await DragDrop.DoDragDropAsync(dragStartEventArgs, dragData, DragDropEffects.Link | DragDropEffects.Move);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStartPoint = null;
        _dragStartEventArgs = null;
        e.Pointer.Capture(null);
    }
}
