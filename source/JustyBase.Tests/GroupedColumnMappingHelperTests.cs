using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class GroupedColumnMappingHelperTests
{
    [Fact]
    public void TryFindMoveIndexes_ShouldResolveIndexes_ForKnownGroupedColumns()
    {
        var groupedProperties = new List<string> { "Fields[2]", "Fields[0]", "Fields[1]" };
        var headers = new List<string> { "ID", "NAME", "CREATED_AT" };

        var result = GroupedColumnMappingHelper.TryFindMoveIndexes(
            groupedProperties,
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
    public void TryFindMoveIndexes_ShouldReturnFalse_WhenAnyColumnIsMissing()
    {
        var groupedProperties = new List<string> { "Fields[0]", "Fields[1]" };
        var headers = new List<string> { "ID", "NAME" };

        var result = GroupedColumnMappingHelper.TryFindMoveIndexes(
            groupedProperties,
            headers,
            "ID",
            "UNKNOWN",
            out int sourceIndex,
            out int targetIndex);

        Assert.False(result);
        Assert.Equal(0, sourceIndex);
        Assert.Equal(-1, targetIndex);
    }

    [Fact]
    public void ToGroupedColumnNames_ShouldIgnoreInvalidPropertyNames()
    {
        var groupedProperties = new List<string> { "Fields[0]", "Fields[999]", "BadValue", "Fields[1]" };
        var headers = new List<string> { "ID", "NAME" };

        var result = GroupedColumnMappingHelper.ToGroupedColumnNames(groupedProperties, headers);

        Assert.Equal(["ID", "NAME"], result);
    }
}
