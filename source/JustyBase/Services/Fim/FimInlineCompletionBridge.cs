using JustyBase.Ai.Fim.Abstractions;
using JustyBase.Ai.Fim.Prompting;
using JustyBase.Editor.InlineCompletion;

namespace JustyBase.Services.Fim;

/// <summary>
/// Bridges editor inline-completion requests to an <see cref="ICompletionProvider"/>.
/// </summary>
public sealed class FimInlineCompletionBridge
{
    private readonly ICompletionProvider _provider;
    private readonly Func<FimPromptBudget> _getBudget;

    public FimInlineCompletionBridge(
        ICompletionProvider provider,
        Func<bool> isEnabled,
        Func<FimPromptBudget>? getBudget = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        IsEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _getBudget = getBudget ?? (() => FimPromptBudget.MediumDefault);
    }

    public Func<bool> IsEnabled { get; }

    /// <summary>Raised after the selected model has been downloaded and loaded.</summary>
    public event EventHandler? ModelReady;

    public void NotifyModelReady() => ModelReady?.Invoke(this, EventArgs.Empty);

    public async Task<string?> CompleteAsync(InlineCompletionContext context, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return null;
        }

        try
        {
            var budget = _getBudget();
            var (promptText, promptCaret) = BuildPromptDocument(context);
            var (prefix, suffix) = FimContextExtractor.Extract(
                promptText,
                promptCaret,
                budget.MaxPromptTokens,
                budget.PrefixPercentage,
                budget.SuffixPercentage);
            if (string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix))
            {
                return null;
            }

            var maxTokens = FimContextExtractor.ClampMaxTokens(budget.MaxGenerationTokens);
            var suggestion = await _provider.CompleteAsync(
                new CompletionRequest(
                    prefix ?? string.Empty,
                    suffix ?? string.Empty,
                    MaxTokens: maxTokens),
                cancellationToken).ConfigureAwait(false);

            return suggestion?.Text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            System.Diagnostics.Debug.WriteLine($"[FIM] CompleteAsync failed: {ex}");
            return null;
        }
    }

    private static (string Text, int CaretOffset) BuildPromptDocument(InlineCompletionContext context)
    {
        var selection = context.CompletionSelection;
        if (selection is null)
        {
            return (context.DocumentText, context.CaretOffset);
        }

        var documentText = context.DocumentText;
        var caret = Math.Clamp(context.CaretOffset, 0, documentText.Length);
        var start = Math.Clamp(selection.ReplacementStartOffset, 0, caret);
        var insertText = selection.InsertText ?? string.Empty;
        var virtualText = string.Concat(documentText[..start], insertText, documentText[caret..]);
        return (virtualText, start + insertText.Length);
    }
}

public readonly record struct FimPromptBudget(
    int MaxPromptTokens,
    double PrefixPercentage,
    double SuffixPercentage,
    int MaxGenerationTokens)
{
    public static FimPromptBudget MediumDefault { get; } = new(1536, 0.65, 0.35, 50);
}
