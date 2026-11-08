using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Tests;

/// <summary>
/// A recursive-descent parser must refuse pathological nesting before the CLR stack overflows:
/// <see cref="StackOverflowException"/> cannot be caught, so a readable parse exception is the only
/// way to keep the editor alive when a generated script contains thousands of parentheses.
/// </summary>
public sealed class NzParserDepthGuardTests
{
    [Fact]
    public void DeeplyNestedParentheses_ThrowReadableDepthException()
    {
        string sql = "SELECT " + new string('(', 400) + "1" + new string(')', 400) + ";";

        var parser = new NzSqlParser(NzLexer.Tokenize(sql).ToArray());

        NzParserDepthException exception = Assert.Throws<NzParserDepthException>(() => parser.Parse());
        Assert.Contains("nested deeper", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NzSqlParser.MaxNestingDepth, exception.Limit);
    }

    [Fact]
    public void DeeplyNestedUnaryOperators_ThrowInsteadOfOverflowing()
    {
        string sql = "SELECT " + string.Concat(Enumerable.Repeat("NOT ", 400)) + "1;";

        var parser = new NzSqlParser(NzLexer.Tokenize(sql).ToArray());

        Assert.Throws<NzParserDepthException>(() => parser.Parse());
    }

    [Fact]
    public void DeeplyNestedDerivedTables_ThrowInsteadOfOverflowing()
    {
        string sql = "SELECT 1";
        for (int i = 0; i < 400; i++)
        {
            sql = $"SELECT * FROM ({sql}) t{i}";
        }

        var parser = new NzSqlParser(NzLexer.Tokenize(sql).ToArray());

        Assert.Throws<NzParserDepthException>(() => parser.Parse());
    }

    [Fact]
    public void ReasonableNesting_StillParses()
    {
        string sql = "SELECT " + new string('(', 40) + "1" + new string(')', 40) + ";";

        var parser = new NzSqlParser(NzLexer.Tokenize(sql).ToArray());

        Assert.NotNull(parser.Parse());
        Assert.Empty(parser.Errors);
    }

    [Fact]
    public void DocumentParseSession_ReportsReadableDiagnosticInsteadOfThrowing()
    {
        string sql = "SELECT " + new string('(', 400) + "1" + new string(')', 400) + ";";
        using var session = new DocumentParseSession(SqlDialect.Netezza);

        var result = session.GetOrParse(sql);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error =>
            error.Code == "PAR102"
            && error.Message.Contains("nested deeper", StringComparison.OrdinalIgnoreCase));
    }
}
