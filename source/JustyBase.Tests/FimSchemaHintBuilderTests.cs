using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Services.Fim;

namespace JustyBase.Tests;

public sealed class FimSchemaHintBuilderTests
{
    private const string DocumentUri = "sql-doc-test";

    private static InMemorySchemaProvider CreateSchema()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("ORDERS", "PUBLIC", Columns:
        [
            new ColumnInfo("ID", DataType: "INT"),
            new ColumnInfo("CUSTOMER_ID", DataType: "INT"),
            new ColumnInfo("STATUS", DataType: "VARCHAR(20)"),
        ]));
        provider.AddTable(new TableInfo("CUSTOMERS", "PUBLIC", Columns:
        [
            new ColumnInfo("ID", DataType: "INT"),
            new ColumnInfo("NAME", DataType: "VARCHAR(100)"),
        ]));
        provider.AddTable(new TableInfo("AUDIT", "PUBLIC", Columns:
        [
            new ColumnInfo("EVENT", DataType: "VARCHAR(50)"),
        ]));
        return provider;
    }

    private static string? Build(InMemorySchemaProvider schema, string sql, int caret, int maxHintChars = 4096)
    {
        using var coordinator = new DocumentParsingCoordinator();
        return FimSchemaHintBuilder.Build(
            coordinator, schema, DocumentUri, SqlDialect.Netezza, sql, caret, maxHintChars);
    }

    [Fact]
    public void Build_SelectWithJoin_ListsTablesWithColumnsAndTypes()
    {
        var schema = CreateSchema();
        const string sql = "SELECT o.ID, c.NAME FROM PUBLIC.ORDERS o JOIN PUBLIC.CUSTOMERS c ON c.ID = o.CUSTOMER_ID WHERE o.STATUS = 'A'";

        var hint = Build(schema, sql, sql.Length);
        var repeat = Build(schema, sql, sql.Length);

        Assert.NotNull(hint);
        Assert.Contains("-- table: PUBLIC.CUSTOMERS(id:int, name:varchar(100))", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-- table: PUBLIC.ORDERS(id:int, customer_id:int, status:varchar(20))", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(hint, repeat);
    }

    [Fact]
    public void Build_CaretInsideSecondStatement_UsesOnlyThatStatement()
    {
        var schema = CreateSchema();
        const string sql = "SELECT * FROM PUBLIC.AUDIT;\nSELECT * FROM PUBLIC.ORDERS";
        var caret = sql.IndexOf("ORDERS", StringComparison.Ordinal) + 3;

        var hint = Build(schema, sql, caret);

        Assert.NotNull(hint);
        Assert.Contains("ORDERS", hint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUDIT", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_Cte_IncludesCteColumnsAndUnderlyingTable()
    {
        var schema = CreateSchema();
        const string sql = "WITH recent(ID) AS (SELECT ID FROM PUBLIC.ORDERS) SELECT * FROM recent";

        var hint = Build(schema, sql, sql.Length);

        Assert.NotNull(hint);
        Assert.Contains("-- cte: recent(id)", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-- table: PUBLIC.ORDERS(", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("UPDATE PUBLIC.ORDERS SET STATUS = 'X' WHERE ID = 1")]
    [InlineData("DELETE FROM PUBLIC.ORDERS WHERE ID = 1")]
    [InlineData("INSERT INTO PUBLIC.ORDERS (ID) VALUES (1)")]
    public void Build_DmlTarget_IsIncluded(string sql)
    {
        var schema = CreateSchema();

        var hint = Build(schema, sql, sql.Length);

        Assert.NotNull(hint);
        Assert.Contains("-- table: PUBLIC.ORDERS(", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_OnlyUnknownTables_ReturnsNull()
    {
        var schema = CreateSchema();
        const string sql = "SELECT * FROM NO_SUCH_TABLE";

        var hint = Build(schema, sql, sql.Length);

        Assert.Null(hint);
    }

    [Fact]
    public void Build_RespectsMaxHintChars()
    {
        var schema = CreateSchema();
        const string sql = "SELECT * FROM PUBLIC.ORDERS o JOIN PUBLIC.CUSTOMERS c ON c.ID = o.CUSTOMER_ID";

        var hint = Build(schema, sql, sql.Length, maxHintChars: 60);

        Assert.NotNull(hint);
        Assert.True(hint.Length <= 60);
        Assert.StartsWith("-- table:", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmptyDocument_ReturnsNull()
    {
        var schema = CreateSchema();

        var hint = Build(schema, string.Empty, 0);

        Assert.Null(hint);
    }

    [Fact]
    public void Build_CaretInCommentBeforeFirstStatement_ReturnsNull()
    {
        var schema = CreateSchema();
        const string sql = "-- header comment\nSELECT * FROM PUBLIC.ORDERS";

        var hint = Build(schema, sql, 5);

        Assert.Null(hint);
    }
}
