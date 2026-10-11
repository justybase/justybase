using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Tests;

public sealed class DatabaseSearchScopeHelperTests
{
    [Fact]
    public void ResolveDatabaseOrFirst_ShouldReturnRequestedDatabase_WhenProvided()
    {
        var result = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst("DB2", ["DB1", "DB2"]);

        Assert.Equal("DB2", result);
    }

    [Fact]
    public void ResolveDatabaseOrFirst_ShouldReturnFirstAvailable_WhenRequestedIsNull()
    {
        var result = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(null, ["DB1", "DB2"]);

        Assert.Equal("DB1", result);
    }

    [Fact]
    public void ResolveDatabaseOrFirst_ShouldReturnNull_WhenNoAvailableDatabases()
    {
        var result = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(null, Array.Empty<string>());

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDatabaseOrFirst_ShouldReturnFirstAvailable_WhenRequestedIsBlank(string requested)
    {
        var result = DatabaseSearchScopeHelper.ResolveDatabaseOrFirst(requested, ["DB1", "DB2"]);

        Assert.Equal("DB1", result);
    }
}
