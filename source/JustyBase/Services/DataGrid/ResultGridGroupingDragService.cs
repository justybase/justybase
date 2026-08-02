namespace JustyBase.Services.DataGrid;

public sealed class ResultGridGroupingDragService : IResultGridGroupingDragService
{
    public Point? CaptureDragStart(bool isLeftButtonPressed, Point pointerPosition)
    {
        return isLeftButtonPressed ? pointerPosition : null;
    }

    public bool ShouldStartDrag(Point? dragStartPoint, bool isLeftButtonPressed, Point currentPosition, double activationDistance = 5.0)
    {
        if (!dragStartPoint.HasValue || !isLeftButtonPressed)
        {
            return false;
        }

        double deltaX = currentPosition.X - dragStartPoint.Value.X;
        double deltaY = currentPosition.Y - dragStartPoint.Value.Y;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        return distance >= activationDistance;
    }

    public bool TryCreateMoveRequest(string? sourceColumnName, string? targetColumnName, out GroupedColumnMoveRequest moveRequest)
    {
        moveRequest = default;
        if (string.IsNullOrWhiteSpace(sourceColumnName) || string.IsNullOrWhiteSpace(targetColumnName))
        {
            return false;
        }

        if (string.Equals(sourceColumnName, targetColumnName, StringComparison.Ordinal))
        {
            return false;
        }

        moveRequest = new GroupedColumnMoveRequest(sourceColumnName, targetColumnName);
        return true;
    }
}
