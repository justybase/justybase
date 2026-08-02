using JustyBase.Services.DataGrid;
using System.Collections.Generic;

namespace JustyBase.Tests;

public sealed class ResultGridGroupingServiceTests
{
    [Fact]
    public void BuildTogglePlan_WhenColumnIsNotGrouped_ReturnsAddAction()
    {
        var service = new ResultGridGroupingService();
        var headers = new List<string> { "ID", "NAME" };
        var grouped = new List<string> { "Fields[0]" };

        var result = service.BuildTogglePlan("NAME", headers, grouped);

        Assert.Equal(GroupingToggleAction.Add, result.Action);
        Assert.Equal("Fields[1]", result.PropertyName);
        Assert.Equal(-1, result.ExistingIndex);
    }

    [Fact]
    public void BuildTogglePlan_WhenColumnIsAlreadyGrouped_ReturnsRemoveActionWithIndex()
    {
        var service = new ResultGridGroupingService();
        var headers = new List<string> { "ID", "NAME", "CITY" };
        var grouped = new List<string> { "Fields[2]", "Fields[1]" };

        var result = service.BuildTogglePlan("CITY", headers, grouped);

        Assert.Equal(GroupingToggleAction.Remove, result.Action);
        Assert.Equal("Fields[2]", result.PropertyName);
        Assert.Equal(0, result.ExistingIndex);
    }

    [Fact]
    public void BuildTogglePlan_WhenColumnIsMissing_ReturnsNone()
    {
        var service = new ResultGridGroupingService();
        var headers = new List<string> { "ID", "NAME" };
        var grouped = new List<string> { "Fields[0]" };

        var result = service.BuildTogglePlan("UNKNOWN", headers, grouped);

        Assert.Equal(GroupingToggleAction.None, result.Action);
    }

    [Fact]
    public void TryFindMoveIndexes_DelegatesToHelper()
    {
        var service = new ResultGridGroupingService();
        var grouped = new List<string> { "Fields[2]", "Fields[0]", "Fields[1]" };
        var headers = new List<string> { "ID", "NAME", "CREATED_AT" };

        bool result = service.TryFindMoveIndexes(
            grouped,
            headers,
            "CREATED_AT",
            "NAME",
            out int sourceIndex,
            out int targetIndex);

        Assert.True(result);
        Assert.Equal(0, sourceIndex);
        Assert.Equal(2, targetIndex);
    }

    [Fact]
    public void ToGroupedColumnNames_DelegatesToHelper()
    {
        var service = new ResultGridGroupingService();
        var grouped = new List<string> { "Fields[0]", "Bad", "Fields[1]" };
        var headers = new List<string> { "ID", "NAME" };

        var result = service.ToGroupedColumnNames(grouped, headers);

        Assert.Equal(["ID", "NAME"], result);
    }
}
