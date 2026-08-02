using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// End-to-end uniqueness checks for completion labels exposed to the SQL editor.
/// </summary>
public sealed class SqlCompletionUniquenessTests
{
    [Fact]
    public void EngineItems_MappedToCompletionDataSql_HaveUniqueTableLabels()
    {
        var schema = CompletionTestAssertions.CreateMultiSchemaDuplicateCatalog();
        var engine = new NzCompletionEngine(schema);
        const string sql = "SELECT * FROM DIMDA";
        var engineItems = engine.GetCompletions(sql, sql.Length);

        var completionData = engineItems
            .Where(item => item.Kind is CompletionKind.Table or CompletionKind.View)
            .Select(item => CompletionDataSql.FromEngineItem(
                item,
                item.Kind == CompletionKind.View ? Glyph.View : Glyph.Table))
            .ToList();

        CompletionTestAssertions.AssertUniqueLabels(completionData.Select(data => data.Text));
        Assert.Equal(1, completionData.Count(data => data.Text.Equals("DIMDATE", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(completionData, data => data.DetailText is "Table" or "View");
    }

    [Fact]
    public void GetTableNames_WithExplicitSchema_DoesNotCollapseDistinctObjects()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("T1", "ADMIN", "JUST_DATA"));
        provider.AddTable(new TableInfo("T2", "ADMIN", "JUST_DATA"));

        var names = provider.GetTableNames("JUST_DATA", "ADMIN")!.Select(item => item.Name).ToArray();

        Assert.Equal(["T1", "T2"], names);
    }
}
