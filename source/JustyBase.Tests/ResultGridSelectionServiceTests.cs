using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridSelectionServiceTests
{
    [Fact]
    public void BuildSelectionChangePlan_WhenManyCellsSelected_ReturnsCellsMessageWithoutRefresh()
    {
        var service = new ResultGridSelectionService();

        var plan = service.BuildSelectionChangePlan(selectedCellsCount: 12, selectedRowsCount: 3, previousSelectedCount: 1);

        Assert.Equal("Selected 12 cells", plan.StatusMessage);
        Assert.False(plan.IsSingleCellSelection);
        Assert.False(plan.ShouldRefreshRowDetails);
        Assert.Equal(1, plan.UpdatedPreviousSelectedCount);
    }

    [Fact]
    public void BuildSelectionChangePlan_WhenSingleCellSelected_FlagsSingleCellBranch()
    {
        var service = new ResultGridSelectionService();

        var plan = service.BuildSelectionChangePlan(selectedCellsCount: 1, selectedRowsCount: 7, previousSelectedCount: 5);

        Assert.Equal("Selected 1 cells", plan.StatusMessage);
        Assert.True(plan.IsSingleCellSelection);
        Assert.False(plan.ShouldRefreshRowDetails);
        Assert.Equal(5, plan.UpdatedPreviousSelectedCount);
    }

    [Fact]
    public void BuildSelectionChangePlan_WhenRowsSelectedCountChanged_RequestsRowDetailsRefresh()
    {
        var service = new ResultGridSelectionService();

        var plan = service.BuildSelectionChangePlan(selectedCellsCount: 0, selectedRowsCount: 4, previousSelectedCount: 2);

        Assert.Equal("Selected 4 rows", plan.StatusMessage);
        Assert.False(plan.IsSingleCellSelection);
        Assert.True(plan.ShouldRefreshRowDetails);
        Assert.Equal(4, plan.UpdatedPreviousSelectedCount);
    }

    [Fact]
    public void BuildSelectionChangePlan_WhenRowsSelectedCountUnchanged_DoesNotRefreshRowDetails()
    {
        var service = new ResultGridSelectionService();

        var plan = service.BuildSelectionChangePlan(selectedCellsCount: 0, selectedRowsCount: 6, previousSelectedCount: 6);

        Assert.False(plan.ShouldRefreshRowDetails);
        Assert.Equal(6, plan.UpdatedPreviousSelectedCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(11, 10)]
    [InlineData(-1, 0)]
    public void GetRowDetailValueColumnCount_ReturnsExpectedCount(int selectedRowsCount, int expected)
    {
        var service = new ResultGridSelectionService();

        int result = service.GetRowDetailValueColumnCount(selectedRowsCount);

        Assert.Equal(expected, result);
    }
}
