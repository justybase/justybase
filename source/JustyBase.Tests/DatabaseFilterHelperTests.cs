using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Tests;

public sealed class DatabaseFilterHelperTests
{
    [Theory]
    [InlineData("SALES", null, true)]
    [InlineData("SALES", "", true)]
    [InlineData("SALES_2024", "sales", true)]
    [InlineData("X_SALES", "sales", true)]
    [InlineData("INVENTORY", "sales", false)]
    public void MatchesPrefixOrUnderscore_ReturnsExpectedValue(string value, string? filter, bool expected)
    {
        var result = DatabaseFilterHelper.MatchesPrefixOrUnderscore(value, filter);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CUSTOMER_NAME", null, true)]
    [InlineData("CUSTOMER_NAME", "", true)]
    [InlineData("CUSTOMER_NAME", "name", true)]
    [InlineData("CUSTOMER_NAME", "cust", true)]
    [InlineData("CUSTOMER_NAME", "order", false)]
    public void MatchesContains_ReturnsExpectedValue(string value, string? filter, bool expected)
    {
        var result = DatabaseFilterHelper.MatchesContains(value, filter);

        Assert.Equal(expected, result);
    }
}
