using JustyBase.Editor.CompletionProviders;
using JustyBase.Helpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlCompletionWhitespaceGateTests
{
    // ===== Provider trigger gate: whitespace never opens the list =====

    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void IsSuppressedTrigger_Whitespace_IsSuppressed(char c)
    {
        Assert.True(SqlCompletionProvider.IsSuppressedTrigger(c));
    }

    [Theory]
    [InlineData('.')]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('1')]
    [InlineData('_')]
    [InlineData('@')]
    public void IsSuppressedTrigger_WordAndDotChars_AreNotSuppressed(char c)
    {
        Assert.False(SqlCompletionProvider.IsSuppressedTrigger(c));
    }

    [Fact]
    public void IsSuppressedTrigger_Null_IsNotSuppressed()
    {
        // null triggerChar == explicit Ctrl+Space → full list is always allowed.
        Assert.False(SqlCompletionProvider.IsSuppressedTrigger(null));
    }

    // ===== GetLastWord: trailing whitespace never leaks into the word =====

    [Fact]
    public void GetLastWord_TrailingSpace_ReturnsEmpty()
    {
        const string sql = "SELECT * FROM T A ";
        var word = EditorHelpers.GetLastWordFromText(sql, sql.Length);
        Assert.Equal(string.Empty, word);
    }

    [Fact]
    public void GetLastWord_AfterWhereColumn_ReturnsDottedWord()
    {
        const string sql = "SELECT * FROM DIMACCOUNT A WHERE A.ACCOUNTCODEALTERNATEK";
        var word = EditorHelpers.GetLastWordFromText(sql, sql.Length);
        Assert.Equal("A.ACCOUNTCODEALTERNATEK", word);
    }

    [Fact]
    public void GetLastWord_MidWord_ReturnsPartialWord()
    {
        var word = EditorHelpers.GetLastWordFromText("SELECT * FROM DIMD", 19);
        Assert.Equal("DIMD", word);
    }

    [Fact]
    public void GetLastWord_StopsAtParenAndComma()
    {
        Assert.Equal(string.Empty, EditorHelpers.GetLastWordFromText("SELECT (", 8));
        Assert.Equal(string.Empty, EditorHelpers.GetLastWordFromText("FROM t1, ", 9));
    }

    [Fact]
    public void GetLastWord_WordAtDocumentStart_IsComplete()
    {
        var word = EditorHelpers.GetLastWordFromText("SELECT 1", 6);
        Assert.Equal("SELECT", word);
    }

    [Fact]
    public void GetLastWord_EmptyOrInvalidInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EditorHelpers.GetLastWordFromText("", 0));
        Assert.Equal(string.Empty, EditorHelpers.GetLastWordFromText("SELECT", 0));
        Assert.Equal(string.Empty, EditorHelpers.GetLastWordFromText("   ", 3));
    }
}
