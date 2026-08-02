using JustyBase.PluginCommons;

namespace JustyBase.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("select '10'","select     ")]
    [InlineData("/*A*/B/*C*/", "     B     ")]
    [InlineData("'aaa'", "     ")]
    [InlineData("\"aaa\"", "     ")]
    [InlineData("", "")]
    [InlineData("\"\"", "  ")]
    [InlineData("''", "  ")]
    [InlineData("'", " ")]
    public void CreateCleanSqlTheory(string input, string expected)
    {
        var result = input.CreateCleanSql();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("--comment\nSELECT", "         \nSELECT")]
    [InlineData("SELECT--comment", "SELECT         ")]
    [InlineData("/*block*/SELECT", "         SELECT")]
    [InlineData("SELECT/*block*/FROM", "SELECT         FROM")]
    [InlineData("'string''value'", "               ")]
    [InlineData("\"id\"", "    ")]
    public void CreateCleanSql_VariousPatterns_ReturnsExpected(string input, string expected)
    {
        var result = input.CreateCleanSql();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("--comment", true)]
    [InlineData("/*block comment*/", true)]
    [InlineData("SELECT 1", false)]
    [InlineData("  --comment only", true)]
    [InlineData("/*comment1*//*comment2*/", true)]
    public void IsAllSqlComment_DetectsCommentsCorrectly(string input, bool expected)
    {
        var result = input.IsAllSqlComment();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("table.column", 1, 5, 5)]
    [InlineData("schema.table.column", 2, 6, 12)]
    [InlineData("no_dots_here", 0, -1, -1)]
    [InlineData("a.b.c.d", 3, 1, 5)]
    public void GetDotsPositionsAndCount_CalculatesCorrectly(string input, int expectedCount, int expectedFirst, int expectedLast)
    {
        input.GetDotsPositionsAndCount(out int lastDot, out int dotCnt, out int firstDot);
        Assert.Equal(expectedCount, dotCnt);
        Assert.Equal(expectedFirst, firstDot);
        Assert.Equal(expectedLast, lastDot);
    }

    [Theory]
    [InlineData("MYTABLE", true, true)]
    [InlineData("mytable", true, false)]
    [InlineData("MYTABLE", false, false)]
    [InlineData("mytable", false, true)]
    [InlineData("SELECT", true, false)]
    [InlineData("MY_TABLE_123", true, true)]
    [InlineData("my-table", true, false)]
    public void IsGoodName_ValidatesSqlNames(string input, bool preferUpper, bool expected)
    {
        var result = input.IsGoodName(preferUpper);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("KOLUMN_A", "KOLUMN_A")]
    [InlineData("Kolumna A", "KOLUMNA_A")]
    [InlineData("123start", "K123START")]
    public void NormalizeDbColumnName_NormalizesCorrectly(string input, string expectedStart)
    {
        var result = input.NormalizeDbColumnName();
        Assert.StartsWith(expectedStart, result);
    }

    [Theory]
    [InlineData("SELECT * FROM table", true, "SELECT * FROM TABLE")]
    [InlineData("SELECT 'value'", true, "SELECT 'value'")]
    [InlineData("select * from table", false, "select * from table")]
    public void ChangeCaseRespectingSqlRules_PreservesStrings(string input, bool upper, string expected)
    {
        var result = input.ChangeCaseRespectingSqlRules(upper);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSqLParts_SplitsLongSql()
    {
        var longSql = new string('A', 10000);
        var result = longSql.GetSqLParts();
        Assert.True(result.Length > 1);
        Assert.Equal(longSql, string.Join("", result));
    }

    [Fact]
    public void DamerauLevenshteinDistance_CalculatesCorrectly()
    {
        Span<char> text1 = "test".ToCharArray();
        var text2 = "tset".AsSpan();
        var distance = text1.DamerauLevenshteinDistance(text2);
        Assert.True(distance >= 0);
    }

    private List<string> _stringsToTest = [
    "ABC/*DEF*/XYZ",
        "/*",
        "*/",
        "'/*'",
        "/**/",
        "/*\n*/",
        "/*\r\n*/",
        "'AAA'B'CCC'",
         "'AAA''CCC'",
         "'",
        "''",
         "'''",
        """
            select 10
            --test
            select 20
        """,
        """
            select 10
            --test
            select 20
        """
    ];

    [Fact]
    public void CreateCleanSqlShouldHaveSameResultAsDifferentImplementationV1()
    {
        foreach (var s in _stringsToTest)
        {
            var expected = CreateCleanSqlAlternativeImplementation(s);
            var result = s.CreateCleanSql();
            Assert.Equal(expected, result);
        }
    }

    private string CreateCleanSqlAlternativeImplementation(string actualString)
    {
        string str = string.Create(actualString.Length, actualString, (chars, buf) =>
        {
            for (int i = 0; i < chars.Length; i++)
            {
                char c = buf[i];

                if (c == '\'')
                {
                    chars[i] = ' ';
                    c = (char)0;
                    i++;
                    while (i < chars.Length && c != '\'')
                    {
                        c = buf[i];
                        chars[i] = ' ';
                        i++;
                    }
                    i--;
                    continue;
                }
                else if (c == '\"')
                {
                    chars[i] = ' ';
                    c = (char)0;
                    i++;
                    while (i < chars.Length && c != '\"')
                    {
                        c = buf[i];
                        chars[i] = ' ';
                        i++;
                    }
                    i--;
                    continue;
                }
                else if (c == '-' && i < chars.Length - 1 && buf[i + 1] == '-')
                {
                    chars[i] = ' ';
                    c = (char)0;
                    i++;
                    while (i < chars.Length && c != '\n')
                    {
                        c = buf[i];
                        if (c != '\r' && c != '\n')
                        {
                            chars[i] = ' ';
                        }
                        else
                        {
                            chars[i] = c;
                        }

                        i++;
                    }
                    i--;
                    continue;
                }
                else if (c == '/' && i < chars.Length - 1 && buf[i + 1] == '*')
                {
                    chars[i] = ' ';
                    c = (char)0;
                    i++;
                    while (i < chars.Length)
                    {
                        c = buf[i];
                        chars[i] = ' ';
                        i++;
                        if (c == '*' && i < chars.Length && buf[i] == '/')
                        {
                            chars[i] = ' ';
                            ++i;
                            break;
                        }
                    }
                    i--;
                    continue;
                }
                chars[i] = c;
            }
        });
        return str;
    }

}
