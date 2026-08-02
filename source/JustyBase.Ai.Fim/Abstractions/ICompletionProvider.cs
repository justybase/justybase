namespace JustyBase.Ai.Fim.Abstractions;

/// <summary>
/// Shared contract for inline AI completions (embedded LLamaSharp FIM, future remote providers).
/// </summary>
public interface ICompletionProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }

    /// <summary>Ensure model/backend is ready (download + load). Safe to call repeatedly.</summary>
    Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<CompletionSuggestion?> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default);
}

public sealed record CompletionRequest(
    string Prefix,
    string Suffix,
    int MaxTokens = 50,
    float Temperature = 0.15f,
    float TopP = 0.9f);

public sealed record CompletionSuggestion(string Text);

public sealed record FimModelProgress(double Fraction, string Message, bool IsIndeterminate = false);
