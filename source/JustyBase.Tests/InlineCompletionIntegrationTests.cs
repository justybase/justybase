using JustyBase.Ai.Fim.Abstractions;
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
