using System.Globalization;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridRowHeaderRulesTests
{
    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "2")]
    [InlineData(8, "9")]
    [InlineData(999, "1,000")]
    [InlineData(12_344, "12,345")]
    public void GetRowHeaderText_IsOneBasedAndGroupedPerCulture(int rowIndex, string expected)
    {
        Assert.Equal(expected, ResultGridRowHeaderRules.GetRowHeaderText(rowIndex, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetRowHeaderText_WithoutCulture_FollowsCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal("12,345", ResultGridRowHeaderRules.GetRowHeaderText(12_344));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void IsOddRow_StripesByDataIndex(int rowIndex, bool expected)
    {
        Assert.Equal(expected, ResultGridRowHeaderRules.IsOddRow(rowIndex));
    }

    [Fact]
    public void IsOddRow_IsStableAcrossVirtualizationRecyclingIndices()
    {
        // Even/odd must follow the data index, never the visual position, so recycled
        // containers keep the same stripe after a scroll.
        for (int rowIndex = 0; rowIndex < 20; rowIndex++)
        {
            Assert.Equal(rowIndex % 2 == 1, ResultGridRowHeaderRules.IsOddRow(rowIndex));
        }
    }

    [Theory]
    [InlineData(0, "(0 Items)")]
    [InlineData(1, "(1 Items)")]
    [InlineData(1234, "(1,234 Items)")]
    public void GroupItemCountFormat_FormatsTheGroupSize(int count, string expected)
    {
        Assert.Equal(
            expected,
            string.Format(CultureInfo.InvariantCulture, ResultGridRowHeaderRules.GroupItemCountFormat, count));
    }
}
