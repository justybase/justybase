using JustyBase.Editor.CompletionProviders;
using JustyBase.Netezza.Completion;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlCompletionProviderLegacyPolicyTests
{
    [Fact]
    public void SqlCompletionProvider_DelegatesToSharedPolicy()
    {
        IReadOnlyList<CompletionItem> engineItems =
            [new CompletionItem("EMPLOYEES", CompletionKind.Table, "table")];
        Assert.Equal(
            SqlCompletionMergePolicy.ShouldRunLegacyPath(engineItems, "SELECT * FROM "),
            SqlCompletionProvider.ShouldRunLegacyPath(engineItems, "SELECT * FROM "));
    }
}
