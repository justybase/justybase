using System.ComponentModel;
using JustyBase.Helpers;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridColumnSortRulesTests
{
    [Theory]
    [InlineData(null, ListSortDirection.Ascending)]
    [InlineData(ListSortDirection.Ascending, ListSortDirection.Descending)]
    [InlineData(ListSortDirection.Descending, ListSortDirection.Ascending)]
    public void NextDirection_TogglesTheGlyph(ListSortDirection? current, ListSortDirection expected)
    {
        Assert.Equal(expected, ResultGridColumnSortRules.NextDirection(current));
    }

    [Fact]
    public void NextDirection_StartingFromUnsorted_AlternatesForever()
    {
        var direction = ResultGridColumnSortRules.NextDirection(null);

        Assert.Equal(ListSortDirection.Ascending, direction);
        Assert.Equal(ListSortDirection.Descending, ResultGridColumnSortRules.NextDirection(direction));
        Assert.Equal(ListSortDirection.Ascending, ResultGridColumnSortRules.NextDirection(ListSortDirection.Descending));
    }

    [Fact]
    public void CanApplyCustomSort_WithCustomResultComparer_ReturnsTrue()
    {
        var comparer = new CustomResultComparer(TypeCode.Int32, 0);

        Assert.True(ResultGridColumnSortRules.CanApplyCustomSort(comparer));
    }

    [Fact]
    public void CanApplyCustomSort_WithForeignComparer_ReturnsFalse()
    {
        // ProDataGrid's built-in sorting must stay untouched for such columns.
        Assert.False(ResultGridColumnSortRules.CanApplyCustomSort(Comparer<string>.Default));
    }

    [Fact]
    public void CanApplyCustomSort_WithoutComparer_ReturnsFalse()
    {
        Assert.False(ResultGridColumnSortRules.CanApplyCustomSort(null));
    }
}
