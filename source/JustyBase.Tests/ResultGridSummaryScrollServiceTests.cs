using Avalonia;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridSummaryScrollServiceTests
{
    [Fact]
    public void SyncHorizontalOffset_UpdatesOnlyHorizontalCoordinate()
    {
        var service = new ResultGridSummaryScrollService();
        var currentOffset = new Vector(5, 9);

        Vector result = service.SyncHorizontalOffset(currentOffset, 20);

        Assert.Equal(20, result.X);
        Assert.Equal(9, result.Y);
    }

    [Fact]
    public void ResolveFirstColumnSpacerWidth_WhenColumnPositionIsKnown_ReturnsPositionWithScrollOffset()
    {
        var service = new ResultGridSummaryScrollService();

        double result = service.ResolveFirstColumnSpacerWidth(fallbackRowHeaderWidth: 45, translatedColumnX: 15, scrollOffsetX: 30);

        Assert.Equal(45, result);
    }

    [Fact]
    public void ResolveFirstColumnSpacerWidth_WhenColumnPositionUnknown_ReturnsFallbackWidth()
    {
        var service = new ResultGridSummaryScrollService();

        double result = service.ResolveFirstColumnSpacerWidth(fallbackRowHeaderWidth: 52, translatedColumnX: null, scrollOffsetX: 30);

        Assert.Equal(52, result);
    }

    [Fact]
    public void ResolveFirstColumnSpacerWidth_WhenCalculatedOffsetIsNegative_ReturnsZero()
    {
        var service = new ResultGridSummaryScrollService();

        double result = service.ResolveFirstColumnSpacerWidth(fallbackRowHeaderWidth: 45, translatedColumnX: -20, scrollOffsetX: 10);

        Assert.Equal(0, result);
    }

    [Fact]
    public void ResolveFirstColumnSpacerWidth_WhenFallbackWidthIsNegative_ReturnsZero()
    {
        var service = new ResultGridSummaryScrollService();

        double result = service.ResolveFirstColumnSpacerWidth(fallbackRowHeaderWidth: -5, translatedColumnX: null, scrollOffsetX: 0);

        Assert.Equal(0, result);
    }
}
