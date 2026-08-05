using System.Diagnostics.CodeAnalysis;
using JustyBase.Core.Diagnostics;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Editor;

/// <summary>
/// Applies semantic foreground colors to SQL tokens in the editor.
/// Delegates classification to <see cref="NzSemanticTokenClassifier"/>.
/// </summary>
public sealed class SemanticLineColorizer : DocumentColorizingTransformer
{
    private static IBrush? CommentBrush;
    private static IBrush? StringBrush;
    private static IBrush? NumberBrush;
    private static IBrush? KeywordBrush;
    private static IBrush? TypeBrush;
    private static IBrush? FunctionBrush;
    private static IBrush? VariableBrush;
    private static IBrush? TableBrush;
    private static IBrush? ColumnBrush;
    private static IBrush? CteBrush;
    private static IBrush? AliasBrush;
    private static IBrush? IdentifierBrush;

    private static Func<SqlDialect, NzSemanticTokenClassifier>? _classifierFactory;
    private static readonly Dictionary<SqlDialect, NzSemanticTokenClassifier> Classifiers = new();
    private static readonly Dictionary<int, string?> DocumentUris = new();
    private static readonly Dictionary<int, TextView?> DocumentViews = new();
    private static readonly Dictionary<int, (string Text, SemanticTokenSpan[] Tokens)> DocumentCache = new();
    private static readonly Dictionary<int, CancellationTokenSource> PendingLexClassification = new();
    private static readonly Dictionary<int, CancellationTokenSource> PendingFullClassification = new();
    private static readonly object CacheLock = new();

    public static void Configure(Func<SqlDialect, NzSemanticTokenClassifier> classifierFactory)
    {
        lock (CacheLock)
        {
            _classifierFactory = classifierFactory;
            Classifiers.Clear();
        }
    }

    private static NzSemanticTokenClassifier GetClassifier(SqlDialect dialect)
    {
        lock (CacheLock)
        {
            if (Classifiers.TryGetValue(dialect, out var cached))
                return cached;

            if (_classifierFactory is null)
                throw new InvalidOperationException("SemanticLineColorizer.Configure was not called before classification.");

            var created = _classifierFactory(dialect);
            Classifiers[dialect] = created;
            return created;
        }
    }

    public static void RegisterDocument(TextDocument document, string? documentUri, TextView? textView = null, SqlDialect dialect = SqlDialect.Netezza)
    {
        lock (CacheLock)
        {
            int docId = document.GetHashCode();
            DocumentUris[docId] = documentUri;
            DocumentViews[docId] = textView;
        }
    }

    public static void SetColors(
        IBrush comment, IBrush str, IBrush number,
        IBrush keyword, IBrush type, IBrush function, IBrush variable,
        IBrush table, IBrush column, IBrush cte, IBrush alias, IBrush identifier)
    {
        CommentBrush = comment;
        StringBrush = str;
        NumberBrush = number;
        KeywordBrush = keyword;
        TypeBrush = type;
        FunctionBrush = function;
        VariableBrush = variable;
        TableBrush = table;
        ColumnBrush = column;
        CteBrush = cte;
        AliasBrush = alias;
        IdentifierBrush = identifier;
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "CTS instances are owned by the scheduled Task (or LexOnly path) and disposed in finally.")]
    public static void ScheduleUpdate(TextDocument document, string? documentUri = null, TextView? textView = null, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (_classifierFactory is null || document is null)
            return;

        var classifier = GetClassifier(dialect);

        string text = document.Text;
        int lineCount = document.LineCount;
        int docId = document.GetHashCode();
        var classificationMode = SqlPerformancePolicy.GetSemanticClassificationMode(lineCount, text.Length);
        bool shouldClassifyLexAsync = classificationMode != SemanticClassificationMode.FullImmediate;

        CancellationTokenSource fullCts = new();
        CancellationTokenSource? lexCts = shouldClassifyLexAsync ? new CancellationTokenSource() : null;
        CancellationToken fullToken = fullCts.Token;
        CancellationToken lexToken = lexCts?.Token ?? CancellationToken.None;
        int debounceMs = SqlPerformancePolicy.GetSemanticDebounceMs(lineCount, text.Length);
        lock (CacheLock)
        {
            DocumentUris[docId] = documentUri;
            if (textView is not null)
                DocumentViews[docId] = textView;
            else
                DocumentViews.TryAdd(docId, null);

            if (!shouldClassifyLexAsync)
            {
                SemanticTokenSpan[] lexTokens;
                using (SqlTypingPerfProbe.Instance.Measure("editor.highlight", "lex", documentUri ?? "semantic-default", text.Length, lineCount))
                {
                    lexTokens = classifier.ClassifyLex(text, documentUri, lineCount);
                }
                DocumentCache[docId] = (text, lexTokens);
            }

            // Cancel superseded work only — do not Dispose here. The owning Task
            // disposes its CTS in finally. Disposing from ScheduleUpdate races with
            // Task.Delay(..., cts.Token) and causes UnobservedTaskException.
            if (PendingLexClassification.TryGetValue(docId, out var previousLexCts))
                CancelQuietly(previousLexCts);
            if (lexCts is not null)
                PendingLexClassification[docId] = lexCts;
            else
                PendingLexClassification.Remove(docId);

            if (PendingFullClassification.TryGetValue(docId, out var previousCts))
                CancelQuietly(previousCts);
            PendingFullClassification[docId] = fullCts;
        }

        SqlTypingPerfProbe.Instance.MarkDocChange(documentUri ?? "semantic-default", text.Length, lineCount);

        if (shouldClassifyLexAsync && lexCts is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await DelayUnlessCanceledAsync(25, lexToken).ConfigureAwait(false))
                        return;

                    var currentState = await Dispatcher.UIThread.InvokeAsync(() =>
                        (Text: document.Text, LineCount: document.LineCount));
                    if (!string.Equals(currentState.Text, text, StringComparison.Ordinal))
                        return;

                    SemanticTokenSpan[] lexTokens;
                    using (SqlTypingPerfProbe.Instance.Measure("editor.highlight", "lex", documentUri ?? "semantic-default", currentState.Text.Length, currentState.LineCount))
                    {
                        lexTokens = classifier.ClassifyLex(currentState.Text, documentUri, currentState.LineCount);
                    }

                    TextView? view;
                    lock (CacheLock)
                    {
                        if (!PendingLexClassification.TryGetValue(docId, out var latestLexCts) || latestLexCts != lexCts || lexToken.IsCancellationRequested)
                            return;

                        DocumentCache[docId] = (currentState.Text, lexTokens);
                        DocumentViews.TryGetValue(docId, out view);
                    }

                    if (view is not null)
                        await Dispatcher.UIThread.InvokeAsync(view.Redraw);
                }
                catch (OperationCanceledException)
                {
                    // Expected when typing continues and older lex run is superseded.
                }
                catch (ObjectDisposedException)
                {
                    // Expected if CTS was disposed while this run was still shutting down.
                }
                finally
                {
                    lock (CacheLock)
                    {
                        if (PendingLexClassification.TryGetValue(docId, out var latestLexCts) && latestLexCts == lexCts)
                            PendingLexClassification.Remove(docId);
                    }
                    lexCts.Dispose();
                }
            });
        }

        if (classificationMode == SemanticClassificationMode.LexOnly)
        {
            lock (CacheLock)
            {
                if (PendingFullClassification.TryGetValue(docId, out var latestCts) && latestCts == fullCts)
                    PendingFullClassification.Remove(docId);
            }
            fullCts.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await DelayUnlessCanceledAsync(debounceMs, fullToken).ConfigureAwait(false))
                    return;

                var currentState = await Dispatcher.UIThread.InvokeAsync(() =>
                    (Text: document.Text, LineCount: document.LineCount));
                string currentText = currentState.Text;
                int currentLineCount = currentState.LineCount;

                if (!string.Equals(currentText, text, StringComparison.Ordinal))
                    return;

                SemanticTokenSpan[] fullTokens;
                using (SqlTypingPerfProbe.Instance.Measure("editor.semantic_tokens", "full", documentUri ?? "semantic-default", currentText.Length, currentLineCount))
                {
                    fullTokens = classifier.ClassifyFull(currentText, documentUri, currentLineCount);
                }

                TextView? view;
                lock (CacheLock)
                {
                    if (!PendingFullClassification.TryGetValue(docId, out var latestCts) || latestCts != fullCts || fullToken.IsCancellationRequested)
                        return;

                    DocumentCache[docId] = (currentText, fullTokens);
                    PendingFullClassification.Remove(docId);
                    DocumentViews.TryGetValue(docId, out view);
                }

                if (view is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(view.Redraw);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when typing continues and newer update supersedes this run.
            }
            catch (ObjectDisposedException)
            {
                // Expected if CTS was disposed while this run was still shutting down.
            }
            finally
            {
                lock (CacheLock)
                {
                    if (PendingFullClassification.TryGetValue(docId, out var latestCts) && latestCts == fullCts)
                        PendingFullClassification.Remove(docId);
                }
                fullCts.Dispose();
            }
        });
    }

    /// <summary>
    /// Debounce delay that returns false on cancel instead of throwing TaskCanceledException
    /// (avoids debugger breaks when "Break when thrown" is enabled for TCE).
    /// </summary>
    private static async Task<bool> DelayUnlessCanceledAsync(int milliseconds, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return false;
        if (milliseconds <= 0)
            return true;

        if (!token.CanBeCanceled)
        {
            await Task.Delay(milliseconds).ConfigureAwait(false);
            return true;
        }

        var delayTask = Task.Delay(milliseconds);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), cancelTcs))
        {
            var completed = await Task.WhenAny(delayTask, cancelTcs.Task).ConfigureAwait(false);
            return completed == delayTask && !token.IsCancellationRequested;
        }
    }

    private static void CancelQuietly(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Owning task already disposed this CTS.
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (CommentBrush is null || _classifierFactory is null)
            return;

        var document = CurrentContext.Document;
        var docText = document.Text;
        var docId = document.GetHashCode();
        SemanticTokenSpan[] tokens;

        lock (CacheLock)
        {
            DocumentUris.TryGetValue(docId, out var documentUri);

            if (!DocumentCache.TryGetValue(docId, out var entry) || entry.Text != docText)
            {
                if (DocumentCache.TryGetValue(docId, out var stale))
                {
                    entry = stale;
                }
                else
                {
                    entry = (docText, Array.Empty<SemanticTokenSpan>());
                    DocumentCache[docId] = entry;
                }
            }

            tokens = entry.Tokens;
        }

        int lineStart = line.Offset;
        int lineEnd = lineStart + line.Length;

        foreach (ref readonly var token in tokens.AsSpan())
        {
            int tokenEnd = token.Start + token.Length;
            if (tokenEnd <= lineStart || token.Start >= lineEnd)
                continue;

            IBrush? brush = token.Kind switch
            {
                SemanticTokenKind.Comment => CommentBrush,
                SemanticTokenKind.String => StringBrush,
                SemanticTokenKind.Number => NumberBrush,
                SemanticTokenKind.Keyword => KeywordBrush,
                SemanticTokenKind.Type => TypeBrush,
                SemanticTokenKind.Function => FunctionBrush,
                SemanticTokenKind.Variable => VariableBrush,
                SemanticTokenKind.Parameter => VariableBrush,
                SemanticTokenKind.Table => TableBrush,
                SemanticTokenKind.Column => ColumnBrush,
                SemanticTokenKind.Cte => CteBrush,
                SemanticTokenKind.Alias => AliasBrush,
                SemanticTokenKind.Identifier => IdentifierBrush,
                _ => null,
            };

            if (brush is null)
                continue;

            int colorStart = Math.Max(token.Start, lineStart);
            int colorEnd = Math.Min(tokenEnd, lineEnd);

            ChangeLinePart(colorStart, colorEnd, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
            });
        }
    }
}
