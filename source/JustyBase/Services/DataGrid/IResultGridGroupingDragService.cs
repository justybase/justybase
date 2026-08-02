namespace JustyBase.Services.DataGrid;

public readonly record struct GroupedColumnMoveRequest(string SourceColumnName, string TargetColumnName);

public interface IResultGridGroupingDragService
{
    Point? CaptureDragStart(bool isLeftButtonPressed, Point pointerPosition);

    bool ShouldStartDrag(Point? dragStartPoint, bool isLeftButtonPressed, Point currentPosition, double activationDistance = 5.0);

    bool TryCreateMoveRequest(string? sourceColumnName, string? targetColumnName, out GroupedColumnMoveRequest moveRequest);
}
