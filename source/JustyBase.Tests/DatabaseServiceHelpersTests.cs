using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;
using Moq;

namespace JustyBase.Tests;

public class DatabaseServiceHelpersTests
{
    [Theory]
    [InlineData("TABLE", TypeInDatabaseEnum.Table)]
    [InlineData("VIEW", TypeInDatabaseEnum.View)]
    [InlineData("PROCEDURE", TypeInDatabaseEnum.Procedure)]
    [InlineData("FUNCTION", TypeInDatabaseEnum.Function)]
    [InlineData("SEQUENCE", TypeInDatabaseEnum.Sequence)]
    [InlineData("SYNONYM", TypeInDatabaseEnum.Synonym)]
    [InlineData("EXTERNAL TABLE", TypeInDatabaseEnum.ExternalTable)]
    [InlineData("INDEX", TypeInDatabaseEnum.Index)]
    [InlineData("PARTITION", TypeInDatabaseEnum.Partition)]
    [InlineData("BASE TABLE", TypeInDatabaseEnum.Table)]
    [InlineData("UNKNOWN_TYPE", TypeInDatabaseEnum.otherNoneGroup)]
    public void GetTypeInDatabaseEnumFromDbName_MapsCorrectly(string input, TypeInDatabaseEnum expected)
    {
        var result = input.GetTypeInDatabaseEnumFromDbName();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(TypeInDatabaseEnum.Table, "Table")]
    [InlineData(TypeInDatabaseEnum.View, "View")]
    [InlineData(TypeInDatabaseEnum.Procedure, "Procedure")]
    [InlineData(TypeInDatabaseEnum.Function, "Function")]
    [InlineData(TypeInDatabaseEnum.Synonym, "Synonym")]
    [InlineData(TypeInDatabaseEnum.ExternalTable, "External table")]
    [InlineData(TypeInDatabaseEnum.Index, "Index")]
    [InlineData(TypeInDatabaseEnum.Partition, "Partition")]
    public void ToStringEx_ConvertsEnumToString(TypeInDatabaseEnum input, string expected)
    {
        var result = input.ToStringEx();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Table", TypeInDatabaseEnum.Table)]
    [InlineData("View", TypeInDatabaseEnum.View)]
    [InlineData("Procedure", TypeInDatabaseEnum.Procedure)]
    [InlineData("Function", TypeInDatabaseEnum.Function)]
    [InlineData("Index", TypeInDatabaseEnum.Index)]
    [InlineData("Partition", TypeInDatabaseEnum.Partition)]
    [InlineData("Unknown", TypeInDatabaseEnum.otherNoneEntry)]
    public void FromStringEx_ConvertsStringToEnum(string input, TypeInDatabaseEnum expected)
    {
        var result = DatabaseServiceHelpers.FromStringEx(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("NetezzaSQL", DatabaseTypeEnum.NetezzaSQL)]
    [InlineData("Postgres", DatabaseTypeEnum.PostgreSql)]
    [InlineData("Oracle", DatabaseTypeEnum.Oracle)]
    [InlineData("MySQL", DatabaseTypeEnum.MySql)]
    [InlineData("SQLite", DatabaseTypeEnum.Sqlite)]
    [InlineData("DuckDB", DatabaseTypeEnum.DuckDB)]
    [InlineData("DB2", DatabaseTypeEnum.DB2)]
    [InlineData("Snowflake", DatabaseTypeEnum.Snowflake)]
    [InlineData("Unknown", DatabaseTypeEnum.NotSupportedDatabase)]
    public void StringToDatabaseTypeEnum_MapsCorrectly(string? input, DatabaseTypeEnum expected)
    {
        var result = DatabaseServiceHelpers.StringToDatabaseTypeEnum(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void StringToDatabaseTypeEnum_ReturnsNotSupportedForNull()
    {
        var result = DatabaseServiceHelpers.StringToDatabaseTypeEnum(null);
        Assert.Equal(DatabaseTypeEnum.NotSupportedDatabase, result);
    }

    [Fact]
    public void GetSupportedDriversNames_ReturnsNonEmptyList()
    {
        var drivers = DatabaseServiceHelpers.GetSupportedDriversNames();
        Assert.NotEmpty(drivers);
        Assert.Contains("Postgres", drivers);
        Assert.Contains("Oracle", drivers);
        Assert.Contains("MySQL", drivers);
        Assert.Contains("Snowflake", drivers);
    }

    [Fact]
    public async Task GetDatabaseService_ConcurrentCallsForSameConnection_ReturnSameCachedInstance()
    {
        string connectionName = $"concurrent-{Guid.NewGuid():N}";
        var databaseService = new Mock<IDatabaseService>();
        databaseService.SetupAllProperties();
        databaseService.Setup(service => service.ClearCachedData());

        using var startGate = new ManualResetEventSlim(false);
        Task<IDatabaseService?>[] tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                return DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: databaseService.Object);
            }))
            .ToArray();

        startGate.Set();
        IDatabaseService?[] results = await Task.WhenAll(tasks);

        Assert.NotNull(results[0]);
        Assert.All(results, result => Assert.Same(results[0], result));

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
    }

    [Fact]
    public void RemoveCachedConnection_RemovesServiceAndClearsCachedData()
    {
        string connectionName = $"cached-{Guid.NewGuid():N}";
        var databaseService = new Mock<IDatabaseService>();
        databaseService.SetupAllProperties();
        databaseService.Setup(service => service.ClearCachedData());

        DatabaseServiceHelpers.GetDatabaseService(null, connectionName, ownDatabaseService: databaseService.Object);

        DatabaseServiceHelpers.RemoveCachedConnection(connectionName);

        databaseService.Verify(service => service.ClearCachedData(), Times.Once);
        Assert.Equal(DatabaseConnectedLevel.NotConnected, DatabaseServiceHelpers.GetDatabaseConnectedLevel(connectionName));
    }
}
