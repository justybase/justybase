using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.Services;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlIntelligenceParityHostTests
{
    [Fact]
    public void ApplyAllSafeFixes_IsExposedForDiagnosticsHost()
    {
        var sql = "select 1;";
        var issues = new[]
        {
            new LintIssue("NZ007", "UPPERCASE", LintSeverity.Information, 0, 6)
        };
        var fixedSql = NzLintCodeActions.ApplyAllSafeFixes(sql, issues);
        Assert.StartsWith("SELECT", fixedSql, StringComparison.Ordinal);
        Assert.True(NzLintCodeActions.IsSafeForFixAll("SQL007"));
    }

    [Fact]
    public void ApplyAllSafeFixes_UppercaseKeyword_MatchesGoldenSnapshot()
    {
        const string sql = "select id from emp;";
        var issues = new[]
        {
            new LintIssue("NZ007", "UPPERCASE", LintSeverity.Information, 0, 6)
        };

        var fixedSql = NzLintCodeActions.ApplyAllSafeFixes(sql, issues);

        Assert.Equal("SELECT id from emp;", fixedSql);
    }

    [Fact]
    public void CompletionItem_InsertText_RoundTripsIntoCompletionDataSql()
    {
        var item = new CompletionItem("COL", CompletionKind.Column, "INTEGER", InsertText: "t.COL");
        var data = CompletionDataSql.FromEngineItem(item, Glyph.Column);
        Assert.Equal("COL", data.Text);
        Assert.Equal("INTEGER", data.DetailText);
        Assert.Equal("INTEGER", data.Description);
    }

    [Fact]
    public void CompletionItem_Column_MapsTypeAndDocumentationToListAndTip()
    {
        var item = new CompletionItem(
            "ACCOUNTTYPE",
            CompletionKind.Column,
            Detail: "VARCHAR(50)",
            Documentation: "Account classification");
        var data = CompletionDataSql.FromEngineItem(item, Glyph.Column);

        Assert.Equal("ACCOUNTTYPE", data.Text);
        Assert.Equal("VARCHAR(50)", data.DetailText);
        Assert.Equal("Account classification", data.DescriptionText);
        Assert.Equal("VARCHAR(50)\nAccount classification", data.Description);
    }

    [Fact]
    public void CompletionItem_Table_MapsKindAsDetailText()
    {
        var item = new CompletionItem("EMPLOYEES", CompletionKind.Table);
        var data = CompletionDataSql.FromEngineItem(item, Glyph.Table);

        Assert.Equal("Table", data.DetailText);
        Assert.Equal("Table", data.Description);
    }

    [Fact]
    public void CompletionItem_FunctionAndExternal_MapDetailText()
    {
        var function = CompletionDataSql.FromEngineItem(
            new CompletionItem("HOURS_BETWEEN", CompletionKind.Function, "HOURS_BETWEEN(...)"),
            Glyph.Function);
        Assert.Equal("HOURS_BETWEEN(...)", function.DetailText);

        var external = CompletionDataSql.FromEngineItem(
            new CompletionItem("EXT_ORDERS", CompletionKind.ExternalTable),
            Glyph.ExternalTable);
        Assert.Equal("External", external.DetailText);
    }

    [Fact]
    public void MetadataCache_ExpiresStaleEntries()
    {
        var cache = new NetezzaMetadataCache();
        cache.MergeTable(new TableInfo("T", "S", "D", Columns: [new ColumnInfo("ID")]), TimeSpan.FromMilliseconds(1));
        WaitUntil(() => !cache.TryGetTable("D", "S", "T", out _), TimeSpan.FromSeconds(2));
        Assert.False(cache.TryGetTable("D", "S", "T", out _));
    }

    [Fact]
    public void LintEngine_SelectStar_IssueSnapshot_IsStable()
    {
        using var engine = new LintEngine();
        var result = engine.RunFullLint(new LintConfig(Sql: "select * from emp;"));

        var snapshot = string.Join(
            "\n",
            result.Issues
                .OrderBy(i => i.RuleId, StringComparer.Ordinal)
                .ThenBy(i => i.StartOffset)
                .Select(i => $"{i.RuleId}|{i.Severity}|{i.StartOffset}|{i.EndOffset}|{i.Message}"));

        Assert.Equal(
            "NZ001|Warning|7|8|NZ001: Avoid using SELECT * - specify explicit column names",
            snapshot);
    }

    [Fact]
    public void MergeAndPublish_PreservesHydratedColumns_WhenRepublishingEmptySnapshot()
    {
        var cache = new NetezzaMetadataCache();
        var schema = new JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider();
        var live = new LiveMetadataSchemaProvider(cache, schema);

        live.MergeAndPublish(new TableInfo("EMP", "PUBLIC", "DB1", Columns: [new ColumnInfo("ID"), new ColumnInfo("NAME")]));
        var epochAfterHydrate = schema.MetadataEpoch;

        live.MergeAndPublish(new TableInfo("EMP", "PUBLIC", "DB1", Columns: []), bumpEpoch: false);

        var table = schema.GetTable("DB1", "PUBLIC", "EMP");
        Assert.NotNull(table);
        Assert.Equal(2, table!.Columns!.Count);
        Assert.Equal(epochAfterHydrate, schema.MetadataEpoch);

        live.PublishEpochBump();
        Assert.Equal(epochAfterHydrate + 1, schema.MetadataEpoch);
    }

    [Fact]
    public void DeferredEmptyColumns_QualifiedOperator_DoesNotEmitSql005()
    {
        // Repro: JUST_DATA.ADMIN.DIMACCOUNT with deferred (empty) columns + alias.OPERATOR
        var schema = new JustyBase.NetezzaSqlParser.Visitor.InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", "ADMIN", "JUST_DATA", Columns: []));
        schema.AddTable(new TableInfo("OTHER", "ADMIN", "JUST_DATA", Columns: [new ColumnInfo("X")]));

        using var engine = new LintEngine();
        var result = engine.RunFullLint(new LintConfig(
            Sql: """
                SELECT A.* FROM
                JUST_DATA.ADMIN.DIMACCOUNT A
                WHERE A.OPERATOR > 0
                """,
            Schema: schema));

        Assert.DoesNotContain(result.Issues, i => i.RuleId == "SQL005");
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Condition not met within {timeout.TotalMilliseconds:0}ms.");
            }

            Thread.Yield();
        }
    }
}
