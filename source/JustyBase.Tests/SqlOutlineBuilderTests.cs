using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class SqlOutlineBuilderTests
{
    [Theory]
    [InlineData(SqlDialect.Netezza)]
    [InlineData(SqlDialect.Db2)]
    [InlineData(SqlDialect.Oracle)]
    [InlineData(SqlDialect.PostgreSql)]
    [InlineData(SqlDialect.MySql)]
    [InlineData(SqlDialect.Sqlite)]
    [InlineData(SqlDialect.Access)]
    public void Build_SplitsPlainStatementsForEveryDialect(SqlDialect dialect)
    {
        var entries = SqlOutlineBuilder.Build("SELECT 1; SELECT 2;", dialect);

        Assert.Equal(2, entries.Count);
        Assert.Equal("SELECT #1", entries[0].Title);
        Assert.Equal("SELECT #2", entries[1].Title);
    }

    [Fact]
    public void Build_UsesDialectLexer()
    {
        // MySQL backtick identifiers are valid for the MySQL lexer and invalid
        // for the Netezza lexer; the outline must follow the document dialect.
        var mysqlEntries = SqlOutlineBuilder.Build("SELECT `a` FROM t;", SqlDialect.MySql);
        var netezzaEntries = SqlOutlineBuilder.Build("SELECT `a` FROM t;", SqlDialect.Netezza);

        Assert.Single(mysqlEntries);
        Assert.Empty(netezzaEntries);
    }
}
