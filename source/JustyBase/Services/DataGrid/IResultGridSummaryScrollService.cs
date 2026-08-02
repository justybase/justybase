namespace JustyBase.Services.DataGrid;

public interface IResultGridSummaryScrollService
{
    Vector SyncHorizontalOffset(Vector currentOffset, double newOffset);

    double ResolveFirstColumnSpacerWidth(double fallbackRowHeaderWidth, double? translatedColumnX, double scrollOffsetX);
}
