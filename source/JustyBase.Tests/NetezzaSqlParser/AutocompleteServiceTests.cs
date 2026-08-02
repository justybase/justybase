using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using Moq;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class AutocompleteServiceTests
{
    private static readonly CurrentAutoCompletDatabaseMode NetezzaAutocompleteMode =
        CurrentAutoCompletDatabaseMode.DatabaseSchemaTable
        | CurrentAutoCompletDatabaseMode.SchemaTable
        | CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional
        | CurrentAutoCompletDatabaseMode.NullSchemaCanBeAccepted;

    private readonly AutocompleteService _service = new();

    [Fact]
    public void SchemaOptionalPrefix_YieldsOneEntryPerShortTableName()
    {
        var database = CreateNetezzaDatabaseMock();
        var results = _service.GetWordsList(
                "DIMDA",
                new Dictionary<string, List<string>>(),
                new Dictionary<string, List<string>>(),
                new Dictionary<string, List<string>>(),
                new Dictionary<string, List<string>>(),
                database,
                "JUST_DATA")
            .ToList();

        CompletionTestAssertions.AssertUniqueLabels(results.Select(r => r.Text));
        Assert.Equal(1, results.Count(r => r.Text.Equals("DIMDATE", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DatabaseDoubleDotPrefix_YieldsUniqueTableNames()
    {
        var database = CreateNetezzaDatabaseMock();
        var results = _service.GetWordsList(
                "JUST_DATA..dimda",
                new Dictionary<string, List<string>>(),
                new Dictionary<string, List<string>>(),
                new Dictionary<string, List<string>>(),
                new Dictionary<string, List<string>>(),
                database,
                "JUST_DATA")
            .ToList();

        CompletionTestAssertions.AssertUniqueLabels(results.Select(r => r.Text));
        Assert.Equal(1, results.Count(r => r.Text.Equals("DIMDATE", StringComparison.OrdinalIgnoreCase)));
    }

    private static IDatabaseService CreateNetezzaDatabaseMock()
    {
        var database = new Mock<IDatabaseService>();
        database.SetupGet(x => x.AutoCompletDatabaseMode).Returns(NetezzaAutocompleteMode);
        database.Setup(x => x.CleanSqlWord(It.IsAny<string?>(), It.IsAny<CurrentAutoCompletDatabaseMode>()))
            .Returns((string? word, CurrentAutoCompletDatabaseMode _) => word ?? string.Empty);

        database.Setup(x => x.GetSchemas("JUST_DATA", ""))
            .Returns(["ADMIN", "PUBLIC", "STAGING"]);
        database.Setup(x => x.GetSchemas("JUST_DATA", It.IsNotIn(""))).Returns([]);

        foreach (var schema in new[] { "ADMIN", "PUBLIC", "STAGING" })
        {
            database.Setup(x => x.GetDbObjects("JUST_DATA", schema, "DIMDA", TypeInDatabaseEnum.Table))
                .Returns([CreateTable("DIMDATE", schema)]);
            database.Setup(x => x.GetDbObjects("JUST_DATA", schema, It.IsNotIn("DIMDA"), TypeInDatabaseEnum.Table))
                .Returns([]);
        }

        database.Setup(x => x.GetDbObjects("JUST_DATA", "", "dimda", TypeInDatabaseEnum.Table))
            .Returns([CreateTable("DIMDATE", "ADMIN")]);
        database.Setup(x => x.GetDbObjects("JUST_DATA", "", It.IsNotIn("dimda"), TypeInDatabaseEnum.Table))
            .Returns([]);

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
