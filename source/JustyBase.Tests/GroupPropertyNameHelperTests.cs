using System;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public class GroupPropertyNameHelperTests
{
    [Fact]
    public void CreatePropertyName_ReturnsExpectedFormat()
    {
        var result = GroupPropertyNameHelper.CreatePropertyName(7);
        Assert.Equal("Fields[7]", result);
    }

    [Fact]
    public void CreatePropertyName_ThrowsForNegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupPropertyNameHelper.CreatePropertyName(-1));
    }

    [Theory]
    [InlineData("Fields[0]", 0)]
    [InlineData("Fields[12]", 12)]
    [InlineData("Fields[999]", 999)]
    public void TryExtractColumnIndex_ReturnsTrueForValidValue(string input, int expectedIndex)
    {
        bool success = GroupPropertyNameHelper.TryExtractColumnIndex(input, out int index);

        Assert.True(success);
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Fields[]")]
    [InlineData("Fields[-1]")]
    [InlineData("Fields[a]")]
    [InlineData("fields[1]")]
    [InlineData("Item[1]")]
    [InlineData("Fields[1] ")]
    public void TryExtractColumnIndex_ReturnsFalseForInvalidValue(string? input)
    {
        bool success = GroupPropertyNameHelper.TryExtractColumnIndex(input, out int index);

        Assert.False(success);
        Assert.Equal(-1, index);
    }
}
