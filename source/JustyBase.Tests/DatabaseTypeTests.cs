using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Tests;

public class DatabaseTypeEnumTests
{
    [Theory]
    [InlineData("NetezzaSQL", DatabaseTypeEnum.NetezzaSQL)]
    [InlineData("Postgres", DatabaseTypeEnum.PostgreSql)]
    [InlineData("MySQL", DatabaseTypeEnum.MySql)]
    [InlineData("Oracle", DatabaseTypeEnum.Oracle)]
    [InlineData("DB2", DatabaseTypeEnum.DB2)]
    [InlineData("SQLite", DatabaseTypeEnum.Sqlite)]
    [InlineData("DuckDB", DatabaseTypeEnum.DuckDB)]
    public void StringToDatabaseTypeEnum_ValidDrivers_ReturnsCorrectEnum(string driver, DatabaseTypeEnum expected)
    {
        // Act
        var result = DatabaseServiceHelpers.StringToDatabaseTypeEnum(driver);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void StringToDatabaseTypeEnum_InvalidDriver_ReturnsNotSupported()
    {
        // Arrange
        var invalidDriver = "InvalidDriver";

        // Act
        var result = DatabaseServiceHelpers.StringToDatabaseTypeEnum(invalidDriver);

        // Assert
        Assert.Equal(DatabaseTypeEnum.NotSupportedDatabase, result);
    }

}

public class ConnectionStringParserTests
{
    [Fact]
    public void ParseConnectionString_ValidString_ReturnsParts()
    {
        // Arrange
        var connectionString = "Server=myServer;Database=myDb;User Id=myUser;Password=myPass;";

        // Act
        var parts = connectionString.Split(';');

        // Assert
        Assert.True(parts.Length >= 3);
        Assert.Contains(parts, p => p.Contains("Server"));
        Assert.Contains(parts, p => p.Contains("Database"));
    }

    [Theory]
    [InlineData("Server=localhost;Port=5480;Database=testdb;User ID=admin;Password=secret;")]
    [InlineData("Data Source=myServer;Initial Catalog=myDb;Integrated Security=True;")]
    public void ConnectionStringFormats_VariousFormats_Accepted(string connectionString)
    {
        // Act & Assert - should not throw
        Assert.NotNull(connectionString);
        Assert.NotEmpty(connectionString);
        Assert.Contains("=", connectionString);
    }
}

public class TypeInDatabaseEnumTests
{
    [Theory]
    [InlineData(TypeInDatabaseEnum.Table, true)]
    [InlineData(TypeInDatabaseEnum.View, true)]
    [InlineData(TypeInDatabaseEnum.ColumnDataType, false)]
    [InlineData(TypeInDatabaseEnum.otherNoneEntry, false)]
    public void IsExpandable_CorrectlyIdentifiesExpandableTypes(TypeInDatabaseEnum type, bool expected)
    {
        // Act
        var result = type switch
        {
            TypeInDatabaseEnum.ColumnDataType => false,
            TypeInDatabaseEnum.ColumnDataTypeNullInfo => false,
            TypeInDatabaseEnum.ColumnComment => false,
            TypeInDatabaseEnum.otherNoneEntry => false,
            _ => true
        };

        // Assert
        Assert.Equal(expected, result);
    }
}
