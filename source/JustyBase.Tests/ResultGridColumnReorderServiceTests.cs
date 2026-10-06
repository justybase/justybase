using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridColumnReorderServiceTests
{
    private readonly ResultGridColumnReorderService _service = new();

    [Theory]
    [InlineData("A", "B", true)]
    [InlineData("Source", "Target", true)]
    public void CanReorderHeaders_WithDifferentHeaders_ReturnsTrue(string source, string target, bool expected)
    {
        Assert.Equal(expected, _service.CanReorderHeaders(source, target));
    }

    [Theory]
    [InlineData(null, "B")]
    [InlineData("A", null)]
    [InlineData("", "B")]
    [InlineData("A", "")]
    [InlineData(null, null)]
    public void CanReorderHeaders_WithMissingHeader_ReturnsFalse(string? source, string? target)
    {
        Assert.False(_service.CanReorderHeaders(source, target));
    }

    [Fact]
    public void CanReorderHeaders_WhenSourceIsTarget_ReturnsFalse()
    {
        Assert.False(_service.CanReorderHeaders("A", "A"));
    }

    [Theory]
    // Drop before the target: the dragged column ends up at the target's index.
    [InlineData(4, 1, false, 5, 1)]
    // ... unless it already sits in front of the target, in which case it stays put.
    [InlineData(0, 1, false, 5, 0)]
    // Drop after the target: the insertion slot shifts by one for everything that
    // originally sat in front of it.
    [InlineData(0, 1, true, 5, 1)]
    [InlineData(4, 1, true, 5, 2)]
    [InlineData(1, 0, true, 5, 1)]
    // Dragging onto the first / last column must stay inside the grid.
    [InlineData(4, 0, false, 5, 0)]
    [InlineData(0, 4, true, 5, 4)]
    // Degenerate cases: no columns, or an index beyond the range.
    [InlineData(0, 0, false, 0, 0)]
    [InlineData(9, 9, true, 5, 4)]
    public void CalculateNewDisplayIndex_ReturnsExpectedIndex(
        int sourceIndex, int targetIndex, bool insertAfter, int columnCount, int expected)
    {
        int actual = _service.CalculateNewDisplayIndex(sourceIndex, targetIndex, insertAfter, columnCount);

        Assert.Equal(expected, actual);
    }
}
