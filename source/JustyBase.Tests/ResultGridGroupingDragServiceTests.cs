using Avalonia;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridGroupingDragServiceTests
{
    [Fact]
    public void CaptureDragStart_WhenLeftButtonPressed_ReturnsPointerPosition()
    {
        var service = new ResultGridGroupingDragService();
        var pointerPosition = new Point(10, 20);

        Point? result = service.CaptureDragStart(isLeftButtonPressed: true, pointerPosition);

        Assert.True(result.HasValue);
        Assert.Equal(pointerPosition, result.Value);
    }

    [Fact]
    public void CaptureDragStart_WhenLeftButtonNotPressed_ReturnsNull()
    {
        var service = new ResultGridGroupingDragService();

        Point? result = service.CaptureDragStart(isLeftButtonPressed: false, new Point(10, 20));

        Assert.Null(result);
    }

    [Fact]
    public void ShouldStartDrag_WhenDistanceBelowThreshold_ReturnsFalse()
    {
        var service = new ResultGridGroupingDragService();

        bool result = service.ShouldStartDrag(new Point(0, 0), isLeftButtonPressed: true, new Point(3, 3));

        Assert.False(result);
    }

    [Fact]
    public void ShouldStartDrag_WhenDistanceAtThreshold_ReturnsTrue()
    {
        var service = new ResultGridGroupingDragService();

        bool result = service.ShouldStartDrag(new Point(0, 0), isLeftButtonPressed: true, new Point(3, 4));

        Assert.True(result);
    }

    [Fact]
    public void TryCreateMoveRequest_ValidDifferentColumns_ReturnsTrueAndRequest()
    {
        var service = new ResultGridGroupingDragService();

        bool result = service.TryCreateMoveRequest("COL_A", "COL_B", out var request);

        Assert.True(result);
        Assert.Equal("COL_A", request.SourceColumnName);
        Assert.Equal("COL_B", request.TargetColumnName);
    }

    [Theory]
    [InlineData(null, "COL_B")]
    [InlineData("COL_A", null)]
    [InlineData("", "COL_B")]
    [InlineData("COL_A", "")]
    [InlineData("COL_A", "COL_A")]
    public void TryCreateMoveRequest_InvalidInput_ReturnsFalse(string? source, string? target)
    {
        var service = new ResultGridGroupingDragService();

        bool result = service.TryCreateMoveRequest(source, target, out _);

        Assert.False(result);
    }
}
