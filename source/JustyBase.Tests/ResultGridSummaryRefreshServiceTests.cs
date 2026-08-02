using JustyBase.Services.DataGrid;
using System.Collections.Generic;

namespace JustyBase.Tests;

public sealed class ResultGridSummaryRefreshServiceTests
{
    [Fact]
    public void HaveColumnWidthsChanged_WhenCacheIsEmpty_ReturnsTrueAndCachesAllWidths()
    {
        var service = new ResultGridSummaryRefreshService();
        var cache = new Dictionary<int, double>();

        bool changed = service.HaveColumnWidthsChanged([100d, 200d], cache);

        Assert.True(changed);
        Assert.Equal(2, cache.Count);
        Assert.Equal(100, cache[0]);
        Assert.Equal(200, cache[1]);
    }

    [Fact]
    public void HaveColumnWidthsChanged_WhenChangesAreWithinTolerance_ReturnsFalse()
    {
        var service = new ResultGridSummaryRefreshService();
        var cache = new Dictionary<int, double>
        {
            [0] = 100.0,
            [1] = 200.0,
        };

        bool changed = service.HaveColumnWidthsChanged([100.3, 199.6], cache, tolerance: 0.5);

        Assert.False(changed);
        Assert.Equal(100.0, cache[0]);
        Assert.Equal(200.0, cache[1]);
    }

    [Fact]
    public void HaveColumnWidthsChanged_WhenOneWidthChangesAboveTolerance_ReturnsTrueAndUpdatesCache()
    {
        var service = new ResultGridSummaryRefreshService();
        var cache = new Dictionary<int, double>
        {
            [0] = 100.0,
            [1] = 200.0,
        };

        bool changed = service.HaveColumnWidthsChanged([100.0, 201.0], cache, tolerance: 0.5);

        Assert.True(changed);
        Assert.Equal(201.0, cache[1]);
    }

    [Fact]
    public void HaveColumnWidthsChanged_WhenColumnsShrink_RemovesStaleCacheEntries()
    {
        var service = new ResultGridSummaryRefreshService();
        var cache = new Dictionary<int, double>
        {
            [0] = 100.0,
            [1] = 200.0,
            [2] = 300.0,
        };

        bool changed = service.HaveColumnWidthsChanged([100.0, 200.0], cache);

        Assert.True(changed);
        Assert.Equal(2, cache.Count);
        Assert.DoesNotContain(2, cache.Keys);
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 5, false)]
    public void ShouldRefreshSummaryRow_ReturnsExpected(bool showSummaryRow, int columnCount, bool expected)
    {
        var service = new ResultGridSummaryRefreshService();

        bool result = service.ShouldRefreshSummaryRow(showSummaryRow, columnCount);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    public void ShouldRefreshGroupHeaderSummaries_ReturnsExpected(int summaryCount, int groupCount, bool expected)
    {
        var service = new ResultGridSummaryRefreshService();

        bool result = service.ShouldRefreshGroupHeaderSummaries(summaryCount, groupCount);

        Assert.Equal(expected, result);
    }
}
