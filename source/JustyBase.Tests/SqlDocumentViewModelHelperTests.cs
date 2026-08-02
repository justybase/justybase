using JustyBase.Helpers.Shared;

namespace JustyBase.Tests;

public class SqlDocumentViewModelHelperTests
{
    // ParseResultTitle – happy path: extracts name when pattern present
    [Theory]
    [InlineData("--REGION RESULT_NAME:MyTab rest of query", "default", "MyTab")]
    [InlineData("--REGION RESULT_NAME:AB rest", "default", "AB")]
    [InlineData("--REGION RESULT_NAME:X trailing text here", "fallback", "X")]
    public void ParseResultTitle_ExtractsNameWhenPatternPresent(string shortQuery, string defaultTitle, string expected)
    {
        var result = SqlDocumentViewModelHelper.ParseResultTitle(shortQuery, defaultTitle);
        Assert.Equal(expected, result);
    }

    // ParseResultTitle – no match: returns default title
    [Theory]
    [InlineData("SELECT 1", "default")]
    [InlineData("--REGION RESULT_NAME:NoTrailingSpace", "default")]  // no space after name → no match
    [InlineData("", "fallback")]
    [InlineData("-- some other comment", "xyz")]
    public void ParseResultTitle_ReturnsDefaultWhenPatternAbsent(string shortQuery, string defaultTitle)
    {
        var result = SqlDocumentViewModelHelper.ParseResultTitle(shortQuery, defaultTitle);
        Assert.Equal(defaultTitle, result);
    }

    // ParseResultTitle – case-sensitive prefix: must start exactly with the prefix
    [Fact]
    public void ParseResultTitle_IsCaseSensitive()
    {
        // lowercase prefix must not match
        var result = SqlDocumentViewModelHelper.ParseResultTitle("--region result_name:X rest", "default");
        Assert.Equal("default", result);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(false, "|SingleBath", true)]
    [InlineData(false, "Grid|SingleBath", true)]
    [InlineData(false, ".xlsb", true)]
    [InlineData(false, ".xlsx", true)]
    [InlineData(false, ".csv", false)]
    public void ShouldRunAsSingleCommand_ReturnsExpectedValue(bool singleCommandEnabled, string? option, bool expected)
    {
        var result = SqlDocumentViewModelHelper.ShouldRunAsSingleCommand(singleCommandEnabled, option);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(".xlsb", true)]
    [InlineData(".parquet", true)]
    [InlineData(".csv", true)]
    [InlineData(".csv.gz", true)]
    [InlineData("grid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RequiresExportPathSelection_ReturnsExpectedValue(string? option, bool expected)
    {
        var result = SqlDocumentViewModelHelper.RequiresExportPathSelection(option);

        Assert.Equal(expected, result);
    }
}
