using JustyBase.Core.Database;
using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using Moq;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class DbWordListProviderTests
{
    private readonly AutocompleteService _service = new();

    [Theory]
    [InlineData(Glyph.Table, SqlWordListKind.Table)]
    [InlineData(Glyph.Column, SqlWordListKind.Column)]
    [InlineData(Glyph.View, SqlWordListKind.View)]
    [InlineData(Glyph.Database, SqlWordListKind.Database)]
    [InlineData(Glyph.Schema, SqlWordListKind.Schema)]
    [InlineData(Glyph.Procedure, SqlWordListKind.Procedure)]
    [InlineData(Glyph.Synonym, SqlWordListKind.Synonym)]
    [InlineData(Glyph.ExternalTable, SqlWordListKind.ExternalTable)]
    [InlineData(Glyph.Function, SqlWordListKind.Function)]
    [InlineData(Glyph.SubQuery, SqlWordListKind.Subquery)]
    [InlineData(Glyph.WithDb, SqlWordListKind.With)]
    [InlineData(Glyph.TempTable, SqlWordListKind.TempTable)]
    [InlineData(Glyph.Snippet, SqlWordListKind.Snippet)]
    public void ToNeutral_maps_glyphs_to_kinds(Glyph glyph, SqlWordListKind expected)
    {
        var item = new CompletionDataSql("LABEL", "desc", false, glyph, null);

        var neutral = DbWordListProvider.ToNeutral(item);

        Assert.Equal("LABEL", neutral.Label);
        Assert.Equal(expected, neutral.Kind);
    }

    [Fact]
    public void ToNeutral_preserves_detail_and_description()
    {
        var item = new CompletionDataSql(
            "DIMDATE", "Table", false, Glyph.Table, null,
            detailText: "Table",
            descriptionText: "dimension date");

        var neutral = DbWordListProvider.ToNeutral(item);

        Assert.Equal("Table", neutral.Detail);
        Assert.Equal("dimension date", neutral.Description);
    }

    [Theory]
    [InlineData("temp table column", SqlWordListKind.Column)]
    [InlineData("subquert column", SqlWordListKind.Column)]
    [InlineData("with column", SqlWordListKind.Column)]
    [InlineData("plain word", SqlWordListKind.Keyword)]
    [InlineData(null, SqlWordListKind.Keyword)]
    public void ToNeutral_classifies_glyphless_items_by_description(string? description, SqlWordListKind expected)
    {
        var item = new CompletionDataSql("X", description ?? string.Empty, false, Glyph.None, null);

        Assert.Equal(expected, DbWordListProvider.ToNeutral(item).Kind);
    }

    [Fact]
    public async Task GetWordsListAsync_resolves_service_and_returns_neutral_items()
    {
        var database = CreateNetezzaDatabaseMock();
        var provider = new DbWordListProvider(_service, _ => database);

        var results = new List<SqlWordListItem>();
        await foreach (var item in provider.GetWordsListAsync(
                           SqlWordListRequest.Empty("DIMDA", "conn", "JUST_DATA")))
        {
            results.Add(item);
        }

        Assert.Contains(results, r => r.Label == "DIMDATE" && r.Kind == SqlWordListKind.Table);
    }

    [Fact]
    public async Task GetWordsListAsync_returns_schema_items()
    {
        var database = CreateNetezzaDatabaseMock();
        var provider = new DbWordListProvider(_service, _ => database);

        var results = new List<SqlWordListItem>();
        await foreach (var item in provider.GetWordsListAsync(
                           SqlWordListRequest.Empty("ADM", "conn", "JUST_DATA")))
        {
            results.Add(item);
        }

        Assert.Contains(results, r => r.Label == "ADMIN" && r.Kind == SqlWordListKind.Schema);
    }

    [Fact]
    public async Task GetWordsListAsync_requires_connection_name()
    {
        var provider = new DbWordListProvider(_service, _ => CreateNetezzaDatabaseMock());

        var results = new List<SqlWordListItem>();
        await foreach (var item in provider.GetWordsListAsync(
                           SqlWordListRequest.Empty("DIMDA")))
        {
            results.Add(item);
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetWordsListAsync_handles_missing_database_service()
    {
        var provider = new DbWordListProvider(_service, _ => null);

        var results = new List<SqlWordListItem>();
        await foreach (var item in provider.GetWordsListAsync(
                           SqlWordListRequest.Empty("DIMDA", "conn", "JUST_DATA")))
        {
            results.Add(item);
        }

        Assert.Empty(results);
    }

    [Fact]
    public void Autocomplete_resolves_unqualified_sqlite_alias_against_main_schema()
    {
        var database = new Mock<IDatabaseService>();
        database.SetupGet(x => x.DatabaseType).Returns(DatabaseTypeEnum.Sqlite);
        database.SetupGet(x => x.AutoCompletDatabaseMode).Returns(
            CurrentAutoCompletDatabaseMode.DatabaseSchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaOptional
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional);
        database.Setup(x => x.CleanSqlWord(It.IsAny<string?>(), It.IsAny<CurrentAutoCompletDatabaseMode>()))
            .Returns((string? word, CurrentAutoCompletDatabaseMode _) => word ?? string.Empty);
        database.Setup(x => x.GetSchemas(It.IsAny<string>(), It.IsAny<string>())).Returns([]);
        database.Setup(x => x.GetDbObjects(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TypeInDatabaseEnum>()))
            .Returns([]);
        database.Setup(x => x.GetColumns("DB", "main", "orders", ""))
            .Returns([new DatabaseColumn("id", null, "INTEGER", false, null)]);

        var aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = ["o"]
        };
        var emptyHints = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var items = _service.GetWordsList(
                "o.", aliases, emptyHints, emptyHints, emptyHints, database.Object, "DB")
            .ToList();

        Assert.Contains(items, item => item.Text == "id" && item.Glyph == Glyph.Column);
        database.Verify(x => x.GetColumns("DB", "main", "orders", ""), Times.AtLeastOnce);
    }

    private static IDatabaseService CreateNetezzaDatabaseMock()
    {
        var database = new Mock<IDatabaseService>();
        database.SetupGet(x => x.AutoCompletDatabaseMode).Returns(
            CurrentAutoCompletDatabaseMode.DatabaseSchemaTable
            | CurrentAutoCompletDatabaseMode.SchemaTable
            | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional
            | CurrentAutoCompletDatabaseMode.NullSchemaCanBeAccepted);
        database.Setup(x => x.CleanSqlWord(It.IsAny<string?>(), It.IsAny<CurrentAutoCompletDatabaseMode>()))
            .Returns((string? word, CurrentAutoCompletDatabaseMode _) => word ?? string.Empty);

        database.Setup(x => x.GetSchemas("JUST_DATA", ""))
            .Returns(["ADMIN", "PUBLIC", "STAGING"]);
        database.Setup(x => x.GetSchemas("JUST_DATA", It.IsNotIn(""))).Returns([]);
        database.Setup(x => x.GetSchemas("JUST_DATA", "ADM")).Returns(["ADMIN"]);

        foreach (var schema in new[] { "ADMIN", "PUBLIC", "STAGING" })
        {
            database.Setup(x => x.GetDbObjects("JUST_DATA", schema, "DIMDA", TypeInDatabaseEnum.Table))
                .Returns([CreateTable("DIMDATE", schema)]);
            database.Setup(x => x.GetDbObjects("JUST_DATA", schema, It.IsNotIn("DIMDA"), TypeInDatabaseEnum.Table))
                .Returns([]);
        }

        foreach (var type in new[]
                 {
                     TypeInDatabaseEnum.View, TypeInDatabaseEnum.Synonym, TypeInDatabaseEnum.Procedure,
                     TypeInDatabaseEnum.ExternalTable
                 })
        {
            database.Setup(x => x.GetDbObjects(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), type))
                .Returns([]);
        }

        database.Setup(x => x.GetDatabases(It.IsAny<string>())).Returns([]);
        return database.Object;
    }

    private static DatabaseObject CreateTable(string name, string schema) =>
        new(1, name, null, TypeInDatabaseEnum.Table, "TABLE", schema, null);
}
