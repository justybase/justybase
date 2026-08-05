using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class NzSqlDocumentFormatterTests
{
    [Fact]
    public void Format_SimpleSelect_UsesNzFormatter()
    {
        var result = NzSqlDocumentFormatter.Format("SELECT 1");
        Assert.Contains("SELECT", result, StringComparison.Ordinal);
        Assert.Contains("1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_InvalidSql_ReturnsOriginalOrPartial()
    {
        const string sql = "SELECT FROM WHERE";
        var result = NzSqlDocumentFormatter.Format(sql);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void Format_SelectFromWhere_MatchesGoldenMultilineSnapshot()
    {
        const string sql = "select id, name from emp where id = 1";
        var result = NzSqlDocumentFormatter.Format(sql);

        const string expected = """
            SELECT id, name
            FROM emp
            WHERE id = 1
            """;

        Assert.Equal(
            NormalizeNewlines(expected.TrimEnd()),
            NormalizeNewlines(result.TrimEnd()));
    }

    [Fact]
    public void Format_TwoStatements_MatchesGoldenSnapshot()
    {
        const string sql = "select 1; select 2;";
        var result = NzSqlDocumentFormatter.Format(sql);

        const string expected = """
            SELECT 1

            SELECT 2
            """;

        Assert.Equal(
            NormalizeNewlines(expected.TrimEnd()),
            NormalizeNewlines(result.TrimEnd()));
    }

    [Fact]
    public void Format_Db2Dialect_KeepsDb2FetchFirstSyntax()
    {
        const string sql = "SELECT id FROM t FETCH FIRST 10 ROWS ONLY";
        var result = NzSqlDocumentFormatter.Format(sql, SqlDialect.Db2);

        Assert.Contains("SELECT", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FETCH FIRST 10 ROWS ONLY", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_Db2Dialect_RejectsNetezzaOnlySyntaxWithoutThrowing()
    {
        const string sql = "SELECT * FROM t LIMIT 10";
        var result = NzSqlDocumentFormatter.Format(sql, SqlDialect.Db2);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
