using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Prompting;
using JustyBase.Editor.InlineCompletion;

namespace JustyBase.Services.Fim;

/// <summary>
/// Bridges editor inline-completion requests to an <see cref="ICompletionProvider"/>.
/// </summary>
public sealed class FimInlineCompletionBridge
{
    private readonly ICompletionProvider _provider;
    private readonly Func<FimPromptBudget> _getBudget;
    private readonly SemaphoreSlim _startGate = new(1, 1);

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

    /// <summary>
    /// Completes inline; <paramref name="schemaHintProvider"/> (per document) may return a
    /// schema-context block (e.g. columns/types of tables near the caret) that is prepended
    /// to the FIM prefix and charged against the prefix budget.
    /// </summary>
    public async Task<string?> CompleteAsync(
        InlineCompletionContext context,
        CancellationToken cancellationToken,
        Func<string, int, string?>? schemaHintProvider = null)
    {
        if (!IsEnabled())
        {
            return null;
        }

        try
        {
            var budget = _getBudget();
            var (promptText, promptCaret) = BuildPromptDocument(context);

            var schemaHint = schemaHintProvider?.Invoke(promptText, promptCaret);

            var (prefix, suffix) = ExtractWithSchemaHint(
                promptText,
                promptCaret,
                budget,
                schemaHint);
            if (string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix))
            {
                return null;
            }

            // Start the server on demand when the model is on disk but the backend is not
            // running (app restart, crash, or a fresh install where Prepare was skipped).
            // Uses CancellationToken.None so a keystroke-triggered debounce cancel cannot
            // abort a server start mid-flight.
            await EnsureServerRunningAsync(cancellationToken).ConfigureAwait(false);
            if (!_provider.IsReady)
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

    private async Task EnsureServerRunningAsync(CancellationToken requestToken)
    {
        if (!_provider.IsAvailable || _provider.IsReady)
        {
            return;
        }

        await _startGate.WaitAsync(requestToken).ConfigureAwait(false);
        try
        {
            if (_provider.IsAvailable && !_provider.IsReady)
            {
                await _provider.EnsureReadyAsync(progress: null, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The request was superseded while waiting behind the gate.
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            System.Diagnostics.Debug.WriteLine($"[FIM] on-demand server start failed: {ex}");
        }
        finally
        {
            _startGate.Release();
        }
    }

    private static (string Prefix, string Suffix) ExtractWithSchemaHint(
        string documentText,
        int caretOffset,
        FimPromptBudget budget,
        string? schemaHint)
    {
        var (prefixLimit, suffixLimit) = FimPresets.ResolveCharBudgets(
            budget.MaxPromptTokens,
            budget.PrefixPercentage,
            budget.SuffixPercentage);

        if (string.IsNullOrWhiteSpace(schemaHint))
        {
            return FimContextExtractor.Extract(documentText, caretOffset, prefixLimit, suffixLimit);
        }

        // Cap the hint to the prefix budget minus the 25% code floor so the injected
        // block never pushes the effective prompt beyond the configured budget.
        var codeFloor = Math.Max(32, prefixLimit / 4);
        var maxHintChars = Math.Max(0, prefixLimit - codeFloor - 1);
        var hint = schemaHint.Length <= maxHintChars ? schemaHint : TruncateAtRuneBoundary(schemaHint, maxHintChars);
        if (hint.Length == 0)
        {
            return FimContextExtractor.Extract(documentText, caretOffset, prefixLimit, suffixLimit);
        }

        var reserved = hint.Length + 1;
        var codeLimit = Math.Max(codeFloor, prefixLimit - reserved);
        var (prefix, suffix) = FimContextExtractor.Extract(documentText, caretOffset, codeLimit, suffixLimit);
        return (hint + "\n" + prefix, suffix);
    }

    /// <summary>Truncates by UTF-16 chars without splitting a surrogate pair.</summary>
    private static string TruncateAtRuneBoundary(string text, int maxChars)
    {
        if (maxChars >= text.Length)
        {
            return text;
        }

        int end = maxChars;
        if (char.IsHighSurrogate(text[end - 1]) && end < text.Length && char.IsLowSurrogate(text[end]))
        {
            end--;
        }

        return text[..end];
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
