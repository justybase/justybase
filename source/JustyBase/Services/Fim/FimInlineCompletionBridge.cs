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

    public async Task<string?> CompleteAsync(InlineCompletionContext context, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return null;
        }

        try
        {
            var budget = _getBudget();
            var (prefix, suffix) = FimContextExtractor.Extract(
                context.DocumentText,
                context.CaretOffset,
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
}

public readonly record struct FimPromptBudget(
    int MaxPromptTokens,
    double PrefixPercentage,
    double SuffixPercentage,
    int MaxGenerationTokens)
{
    public static FimPromptBudget MediumDefault { get; } = new(1536, 0.65, 0.35, 50);
}
