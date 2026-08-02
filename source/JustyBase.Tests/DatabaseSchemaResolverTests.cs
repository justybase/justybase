using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Tests;

public class DatabaseSchemaResolverTests
{
    [Fact]
    public void GetAvailableSchemas_ExplicitSchema_ReturnsThatSchema()
    {
        var defSchema = new Dictionary<string, string>();
        var schemaTable = new Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>>();

        var result = DatabaseSchemaResolver.GetAvailableSchemas("MYDB", "PUBLIC", defSchema, schemaTable);

        Assert.Single(result);
        Assert.Equal("PUBLIC", result[0]);
    }

    [Fact]
    public void GetAvailableSchemas_EmptySchema_UsesDefaultSchema()
    {
        var defSchema = new Dictionary<string, string> { ["MYDB"] = "ADMIN" };
        var schemaTable = new Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>>();

        var result = DatabaseSchemaResolver.GetAvailableSchemas("MYDB", "", defSchema, schemaTable);

        Assert.Single(result);
        Assert.Equal("ADMIN", result[0]);
    }

    [Fact]
    public void GetAvailableSchemas_SystemDatabase_ReturnsAllSystemSchemas()
    {
        var defSchema = new Dictionary<string, string>();
        var schemaTable = new Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>>
        {
            ["SYSTEM"] = new()
            {
                ["DEFINITION_SCHEMA"] = [],
                ["MANAGEMENT"] = []
            }
        };

        var result = DatabaseSchemaResolver.GetAvailableSchemas("SYSTEM", "", defSchema, schemaTable);

        Assert.Equal(2, result.Count);
        Assert.Contains("DEFINITION_SCHEMA", result);
        Assert.Contains("MANAGEMENT", result);
    }

    [Fact]
    public void GetAvailableSchemas_EmptySchemaNoDefault_ReturnsEmpty()
    {
        var defSchema = new Dictionary<string, string>();
        var schemaTable = new Dictionary<string, Dictionary<string, Dictionary<string, DatabaseObject>>>();

        var result = DatabaseSchemaResolver.GetAvailableSchemas("UNKNOWN", null, defSchema, schemaTable);

        Assert.Empty(result);
    }

    [Fact]
    public void GetCleanedNames_ThreePart_QuotesAll()
    {
        var (db, schema, table) = DatabaseSchemaResolver.GetCleanedNames(
            "mydb", "public", "users",
            name => $"\"{name}\"");

        Assert.Equal("\"mydb\"", db);
        Assert.Equal("\"public\"", schema);
        Assert.Equal("\"users\"", table);
    }

    [Fact]
    public void GetCleanedNames_TwoPart_QuotesAll()
    {
        var (schema, table) = DatabaseSchemaResolver.GetCleanedNames(
            "admin", "orders",
            name => $"[{name}]");

        Assert.Equal("[admin]", schema);
        Assert.Equal("[orders]", table);
    }

    [Fact]
    public void GetCleanedNames_NullInputs_PreservesNulls()
    {
        var (db, schema, table) = DatabaseSchemaResolver.GetCleanedNames(
            null!, null!, null!,
            name => $"\"{name}\"");

        Assert.Null(db);
        Assert.Null(schema);
        Assert.Null(table);
    }
}
