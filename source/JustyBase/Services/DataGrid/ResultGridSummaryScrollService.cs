namespace JustyBase.Services.DataGrid;

public sealed class ResultGridSummaryScrollService : IResultGridSummaryScrollService
{
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
}
