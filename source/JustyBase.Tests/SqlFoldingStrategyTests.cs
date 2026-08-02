using AvaloniaEdit.Document;
using JustyBase.Editor.Folding;

namespace JustyBase.Tests;

public sealed class SqlFoldingStrategyTests
{
    [Fact]
    public void CreateNewFoldings_RegionPair_ProducesNamedFolding()
    {
        var document = new TextDocument("""
            --region helpers
            SELECT 1;
            --endregion
            """);

        var strategy = new SqlFoldingStrategy();
        var foldings = strategy.CreateNewFoldings(document, out int firstErrorOffset).ToList();

        Assert.Equal(-1, firstErrorOffset);
        Assert.Single(foldings);
        Assert.Equal(0, foldings[0].StartOffset);
        Assert.True(foldings[0].EndOffset > foldings[0].StartOffset);
        Assert.Contains("REGION", foldings[0].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateNewFoldings_WithoutRegions_ReturnsEmpty()
    {
        var document = new TextDocument("SELECT 1;");
        var strategy = new SqlFoldingStrategy();

        var foldings = strategy.CreateNewFoldings(document, out _).ToList();

        Assert.Empty(foldings);
    }

    [Fact]
    public void CreateNewFoldings_NestedRegions_ReturnsOuterThenInnerByStart()
    {
        var document = new TextDocument("""
            --region outer
            --region inner
            SELECT 1;
            --endregion
            --endregion
            """);

        var strategy = new SqlFoldingStrategy();
        var foldings = strategy.CreateNewFoldings(document).ToList();

        Assert.Equal(2, foldings.Count);
        Assert.True(foldings[0].StartOffset < foldings[1].StartOffset);
        Assert.True(foldings[0].EndOffset > foldings[1].EndOffset);
    }
}
