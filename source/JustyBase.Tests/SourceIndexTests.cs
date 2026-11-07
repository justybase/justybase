using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.Services.Schema;

namespace JustyBase.Tests;

/// <summary>
/// Port of <c>crates/app-core/src/source_index.rs</c> tests. The index must use the
/// SQL lexer so strings/comments and table positions get different confidence.
/// </summary>
public sealed class SourceIndexTests
{
    private static SourceDocument Entry(string schema, string name, string objectType, string text)
        => new("JUST_DATA", schema, name, objectType == "PROCEDURE" ? $"{name}()" : null, objectType, text);

    private static SourceTarget Target(string name)
        => new("JUST_DATA", "ADMIN", name);

    private static SourceIndex Index(params SourceDocument[] entries)
        => SourceIndex.Build(entries, SqlDialect.Netezza);

    [Fact]
    public void NameInTablePositionIsParsedAndStringIsText()
    {
        var index = Index(
            Entry("ADMIN", "V_SALES", "VIEW", "SELECT * FROM CUSTOMER C JOIN ORDERS O ON O.ID = C.ID"),
            Entry("ADMIN", "CUSTOMER_DOTNET", "PROCEDURE", "BEGIN RAISE NOTICE 'The customer name is alpha'; END;"));

        var usages = index.Usages(Target("CUSTOMER"));

        Assert.Equal(2, usages.Count);
        Assert.Equal("V_SALES", usages[0].Object.Name);
        Assert.Equal(SourceConfidence.Parsed, usages[0].Confidence);
        Assert.Equal("CUSTOMER_DOTNET", usages[1].Object.Name);
        Assert.Equal(SourceConfidence.Text, usages[1].Confidence);
    }

    [Fact]
    public void QualificationDecidesWhichObjectIsMeant()
    {
        var index = Index(
            Entry("ADMIN", "V_OTHER_SCHEMA", "VIEW", "SELECT * FROM STAGING.CUSTOMER"),
            Entry("ADMIN", "V_QUALIFIED", "VIEW", "SELECT * FROM ADMIN.CUSTOMER"),
            Entry("ADMIN", "V_THREE_PART", "VIEW", "SELECT * FROM JUST_DATA.ADMIN.CUSTOMER"),
            Entry("ADMIN", "V_OTHER_DB", "VIEW", "SELECT * FROM OTHER_DB..CUSTOMER"),
            Entry("ADMIN", "V_DOUBLE_DOT", "VIEW", "SELECT * FROM JUST_DATA..CUSTOMER"));

        var names = index.Usages(Target("CUSTOMER")).Select(hit => hit.Object.Name).ToArray();

        Assert.Contains("V_QUALIFIED", names);
        Assert.Contains("V_THREE_PART", names);
        Assert.Contains("V_DOUBLE_DOT", names);
        Assert.DoesNotContain("V_OTHER_SCHEMA", names);
        Assert.DoesNotContain("V_OTHER_DB", names);
    }

    [Fact]
    public void UnqualifiedNameInAnotherSchemaIsOnlyLikely()
    {
        var view = Entry("REPORTING", "V_REPORT", "VIEW", "SELECT * FROM CUSTOMER");

        var usages = Index(view).Usages(Target("CUSTOMER"));

        Assert.Single(usages);
        Assert.Equal(SourceConfidence.Likely, usages[0].Confidence);
    }

    [Fact]
    public void ColumnWithSameNameIsNotAConfidentReference()
    {
        var index = Index(Entry("ADMIN", "V_COLUMNS", "VIEW", "SELECT CUSTOMER, 1 FROM ORDERS"));

        var usages = index.Usages(Target("CUSTOMER"));

        Assert.Single(usages);
        Assert.Equal(SourceConfidence.Likely, usages[0].Confidence);
    }

    [Fact]
    public void CallsCommentsAndDynamicSqlAreFound()
    {
        var index = Index(
            Entry("ADMIN", "P_CALLER", "PROCEDURE", "BEGIN\n CALL LOAD_CUSTOMERS();\nEND;"),
            Entry("ADMIN", "P_COMMENT", "PROCEDURE", "BEGIN\n -- old: load_customers\nEND;"),
            Entry("ADMIN", "P_DYNAMIC", "PROCEDURE", "BEGIN EXECUTE IMMEDIATE 'CALL load_customers()'; END;"));

        var usages = index.Usages(Target("LOAD_CUSTOMERS"));

        Assert.Equal(3, usages.Count);
        Assert.Equal("P_CALLER", usages[0].Object.Name);
        Assert.Equal(SourceConfidence.Parsed, usages[0].Confidence);
        Assert.Equal(2, usages[0].Line);
        Assert.Contains("CALL LOAD_CUSTOMERS", usages[0].Snippet);
        Assert.All(usages.Skip(1), hit => Assert.Equal(SourceConfidence.Text, hit.Confidence));
    }

    [Fact]
    public void LongerNameThatMerelyContainsTargetIsNotAReference()
    {
        var index = Index(
            Entry("ADMIN", "V_X", "VIEW", "SELECT * FROM CUSTOMER_HISTORY WHERE note = 'CUSTOMERS'"));

        Assert.Empty(index.Usages(Target("CUSTOMER")));
    }

    [Fact]
    public void ImpactIsBreadthFirstAndStopsAtTheDepthLimit()
    {
        var index = Index(
            Entry("ADMIN", "V_ONE", "VIEW", "SELECT * FROM CUSTOMER"),
            Entry("ADMIN", "V_TWO", "VIEW", "SELECT * FROM V_ONE"),
            Entry("ADMIN", "P_THREE", "PROCEDURE", "CALL V_TWO();"));

        var (nodes, truncated) = index.Impact(Target("CUSTOMER"), maxDepth: 2, maxNodes: 100);

        Assert.False(truncated);
        Assert.Equal(2, nodes.Count);
        Assert.Equal("V_ONE", nodes[0].Hit.Object.Name);
        Assert.Equal(1, nodes[0].Depth);
        Assert.Null(nodes[0].Parent);
        Assert.Equal("V_TWO", nodes[1].Hit.Object.Name);
        Assert.Equal(2, nodes[1].Depth);
        Assert.Equal(0, nodes[1].Parent);
    }

    [Fact]
    public void ImpactReportsTruncationAtNodeLimit()
    {
        var index = Index(
            Entry("ADMIN", "V_ONE", "VIEW", "SELECT * FROM CUSTOMER"),
            Entry("ADMIN", "V_TWO", "VIEW", "SELECT * FROM CUSTOMER"));

        var (nodes, truncated) = index.Impact(Target("CUSTOMER"), maxDepth: 3, maxNodes: 1);

        Assert.True(truncated);
        Assert.Single(nodes);
    }

    [Fact]
    public void SearchSupportsCaseWholeWordRegexAndCommentMasking()
    {
        var index = Index(
            Entry("ADMIN", "V_SALES", "VIEW", "SELECT customer_id FROM sales -- other note"),
            Entry("ADMIN", "P_NOTE", "PROCEDURE", "BEGIN -- customer only\n NULL;\nEND;"),
            Entry("ADMIN", "V_CODE", "VIEW", "SELECT customer FROM sales"));

        Assert.Equal(3, index.Search(new SourceTextQuery { Term = "customer" }).Count);
        Assert.Equal(2, index.Search(new SourceTextQuery { Term = "customer", WholeWord = true }).Count);
        Assert.Equal(2, index.Search(new SourceTextQuery { Term = "^SELECT", Regex = true, CaseSensitive = true }).Count);
        Assert.Single(index.Search(new SourceTextQuery { Term = "customer", IgnoreComments = true, WholeWord = true }));
        Assert.Empty(index.Search(new SourceTextQuery { Term = "CUSTOMER", CaseSensitive = true }));
    }

    [Fact]
    public void SearchFiltersObjectTypeAndSchemaAndReturnsLineAndSnippet()
    {
        var index = Index(
            Entry("ADMIN", "V_SALES", "VIEW", "SELECT 1\nFROM customer"),
            Entry("ADMIN", "P_LOAD", "PROCEDURE", "SELECT 1\nFROM customer"),
            Entry("STAGING", "V_STAGE", "VIEW", "SELECT 1\nFROM customer"));

        var hits = index.Search(new SourceTextQuery
        {
            Term = "FROM customer",
            ObjectTypes = ["VIEW"],
            Schemas = ["ADMIN"],
        });

        var hit = Assert.Single(hits);
        Assert.Equal("V_SALES", hit.Object.Name);
        Assert.Equal(2, hit.Line);
        Assert.Contains("FROM customer", hit.Snippet);
        Assert.Equal(1, hit.Matches);
    }

    [Fact]
    public void InvalidRegexIsReportedInsteadOfSilentlyMatchingEverything()
    {
        var index = Index(Entry("ADMIN", "V_SALES", "VIEW", "SELECT 1"));

        var error = Assert.Throws<ArgumentException>(
            () => index.Search(new SourceTextQuery { Term = "(", Regex = true }));

        Assert.Contains("Invalid regular expression", error.Message);
    }
}
