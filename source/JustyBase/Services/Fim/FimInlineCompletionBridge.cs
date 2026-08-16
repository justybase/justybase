using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Prompting;
using JustyBase.Editor.InlineCompletion;

namespace JustyBase.Services.Fim;

/// <summary>
/// Bridges editor inline-completion requests to an <see cref="ICompletionProvider"/>.
/// </summary>
public sealed class FimInlineCompletionBridge : IDisposable
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
    /// Best-effort background preload: starts the llama-server only when the model is
    /// already on disk (never triggers a download from the editor hot path). Serialized
    /// with the on-demand start so concurrent keystrokes and preloads never race.
    /// </summary>
    public async Task<bool> TryPreloadAsync(CancellationToken cancellationToken = default)
    {
        if (_provider.IsReady)
        {
            return true;
        }

        if (!_provider.IsAvailable)
        {
            return false;
        }

        try
        {
            await _startGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!_provider.IsReady)
                {
                    await _provider.EnsureReadyAsync(progress: null, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _startGate.Release();
            }
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }

        return _provider.IsReady;
    }


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

    public void Dispose()
    {
        _startGate.Dispose();
        GC.SuppressFinalize(this);
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
        if (maxChars <= 0)
        {
            return string.Empty;
        }

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
        // Keep the real document and the real caret even when a completion list item is
        // selected. The model is asked to continue from the typed prefix, so its output
        // starts with the item remainder ("ENDARSEMESTER = 1;") and the ghost composer's
        // augmentation rule (VS Code) can match it against the selected seed. Expanding
        // the item into the prompt would make the model continue after the item, the
        // output would not start with the seed, and the ghost would never render.
        return (context.DocumentText, Math.Clamp(context.CaretOffset, 0, context.DocumentText.Length));
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
