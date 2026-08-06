using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Editor;
using JustyBase.Editor.InlineCompletion;
using JustyBase.Services.Fim;

namespace JustyBase.Tests;

public sealed class InlineCompletionIntegrationTests
{
    [Fact]
    public async Task FimBridge_UsesSelectedCompletionInVirtualPrefix()
    {
        var provider = new RecordingCompletionProvider(" = 1;");
        var bridge = new FimInlineCompletionBridge(provider, () => true);
        const string document = "SELECT * FROM DIMDATE D WHERE D.CAL";
        var replacementStart = document.Length - 3;

        var context = new InlineCompletionContext(
            document,
            document.Length,
            new CompletionSelectionSnapshot(
                InsertText: "CALENDARSEMESTER",
                ReplacementStartOffset: replacementStart,
                ReplacementEndOffset: document.Length));

        var result = await bridge.CompleteAsync(context, CancellationToken.None);

        Assert.Equal(" = 1;", result);
        Assert.Contains("D.CALENDARSEMESTER", provider.Request!.Prefix, StringComparison.Ordinal);
        Assert.DoesNotContain("D.CALCALENDARSEMESTER", provider.Request.Prefix, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionSelectionSnapshot_StoresReplacementRange()
    {
        var selection = new CompletionSelectionSnapshot("CALENDARSEMESTER", 42, 45);

        Assert.Equal("CALENDARSEMESTER", selection.InsertText);
        Assert.Equal(42, selection.ReplacementStartOffset);
        Assert.Equal(45, selection.ReplacementEndOffset);
    }

    [Fact]
    public async Task FimBridge_WithSchemaHintProvider_PrependsHintAndKeepsCodePrefix()
    {
        var provider = new RecordingCompletionProvider(";");
        var bridge = new FimInlineCompletionBridge(provider, () => true);
        const string document = "SELECT * FROM ORDERS WHERE ST";
        Func<string, int, string?> hintProvider = (_, _) => "-- table: PUBLIC.ORDERS(id:int)";

        var context = new InlineCompletionContext(document, document.Length);
        await bridge.CompleteAsync(context, CancellationToken.None, hintProvider);

        Assert.NotNull(provider.Request);
        Assert.StartsWith("-- table: PUBLIC.ORDERS(id:int)", provider.Request!.Prefix, StringComparison.Ordinal);
        Assert.Contains("SELECT * FROM ORDERS", provider.Request.Prefix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FimBridge_SchemaHint_IsChargedAgainstPrefixBudget()
    {
        var provider = new RecordingCompletionProvider(";");
        var bridge = new FimInlineCompletionBridge(
            provider,
            () => true,
            () => new FimPromptBudget(MaxPromptTokens: 128, 0.65, 0.35, 50));
        const string document = "SELECT * FROM ORDERS WHERE STATUS = 'A'";
        var hint = new string('x', 400);
        Func<string, int, string?> hintProvider = (_, _) => hint;

        var context = new InlineCompletionContext(document, document.Length);
        await bridge.CompleteAsync(context, CancellationToken.None, hintProvider);

        Assert.NotNull(provider.Request);
        var prefix = provider.Request!.Prefix;
        Assert.StartsWith(hint, prefix, StringComparison.Ordinal);
        var codePart = prefix[(hint.Length + 1)..];
        Assert.NotEmpty(codePart);
        // 128 tokens * 4 chars * 65% prefix ≈ 333 chars; code keeps the 25% floor (≈83).
        Assert.True(codePart.Length <= 83, $"code window too large: {codePart.Length}");
    }

    [Fact]
    public async Task FimBridge_NoHint_DoesNotChangePrompt()
    {
        var provider = new RecordingCompletionProvider(";");
        var bridge = new FimInlineCompletionBridge(provider, () => true);
        const string document = "SELECT * FROM ORDERS";

        var context = new InlineCompletionContext(document, document.Length);
        await bridge.CompleteAsync(context, CancellationToken.None, (_, _) => null);

        Assert.NotNull(provider.Request);
        Assert.Equal(document, provider.Request!.Prefix);
    }

    private sealed class RecordingCompletionProvider(string response) : ICompletionProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public bool IsAvailable => true;
        public CompletionRequest? Request { get; private set; }

        public Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CompletionSuggestion?> CompleteAsync(
            CompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult<CompletionSuggestion?>(new CompletionSuggestion(response));
        }
    }
}
