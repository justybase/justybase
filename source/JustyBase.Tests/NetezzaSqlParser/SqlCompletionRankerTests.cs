using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlCompletionRankerTests
{
    [Fact]
    public void GetMatchRank_PrefixBeatsSubstring()
    {
        Assert.Equal(0, SqlCompletionRanker.GetMatchRank("DIMD", "DIMDATE"));
        Assert.Equal(3, SqlCompletionRanker.GetMatchRank("MDAT", "DIMDATE"));
        Assert.True(
            SqlCompletionRanker.GetMatchRank("DIMD", "DIMDATE") <
            SqlCompletionRanker.GetMatchRank("MDAT", "DIMDATE"));
    }

    [Fact]
    public void GetMatchRank_AcronymMatches()
    {
        Assert.Equal(2, SqlCompletionRanker.GetMatchRank("DD", "DIM_DATE"));
    }

    [Fact]
    public void GetMatchRank_EmptyPrefix_MatchesAll()
    {
        Assert.Equal(0, SqlCompletionRanker.GetMatchRank(string.Empty, "DIMDATE"));
        Assert.Equal(0, SqlCompletionRanker.GetMatchRank(null, "DIMDATE"));
    }

    [Fact]
    public void SortAndDeduplicate_DedupsCaseInsensitiveAndRanksPrefixFirst()
    {
        var items = new List<ICompletionDataEx>
        {
            new CompletionDataSql("DIMDATE", "Table", false, Glyph.Table, null),
            new CompletionDataSql("dimdate", "Table", false, Glyph.Table, null),
            new CompletionDataSql("ADIMDATE_X", "Table", false, Glyph.Table, null),
            new CompletionDataSql("DIMDATETIME", "Table", false, Glyph.Table, null),
        };

        var sorted = SqlCompletionRanker.SortAndDeduplicate(items, "DIMD");

        Assert.Equal(3, sorted.Count);
        Assert.Equal("DIMDATE", sorted[0].Text);
        Assert.Equal("DIMDATETIME", sorted[1].Text);
    }

    [Fact]
    public void SortText_DoesNotThrow_AndIsStable()
    {
        var item = new CompletionDataSql("DIMDATE", "Table", false, Glyph.Table, null);
        Assert.False(string.IsNullOrWhiteSpace(item.SortText));
        Assert.StartsWith("01_", item.SortText, StringComparison.Ordinal);
    }
}
