using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public static class CompletionTestAssertions
{
    public static void AssertUniqueLabels(IEnumerable<string> labels)
    {
        var duplicateGroups = labels
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} x{group.Count()}")
            .ToArray();

        Assert.True(duplicateGroups.Length == 0,
            "Duplicate completion labels: " + string.Join(", ", duplicateGroups));
    }

    public static void AssertUniqueTableAndViewLabels(IReadOnlyList<CompletionItem> items)
    {
        var labels = items
            .Where(item => item.Kind is CompletionKind.Table or CompletionKind.View)
            .Select(item => item.Label);
        AssertUniqueLabels(labels);
    }

    public static void AssertLabelCount(IReadOnlyList<CompletionItem> items, string label, int expectedCount)
    {
        var actual = items.Count(item =>
            item.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedCount, actual);
    }

    public static InMemorySchemaProvider CreateMultiSchemaDuplicateCatalog()
    {
        var provider = new InMemorySchemaProvider();
        foreach (var schema in new[] { "ADMIN", "PUBLIC", "STAGING" })
        {
            provider.AddTable(new TableInfo("DIMDATE", schema, "JUST_DATA"));
        }
        provider.AddTable(new TableInfo("OTHER_DB_TABLE", "ADMIN", "OTHER_DB"));
        return provider;
    }
}
