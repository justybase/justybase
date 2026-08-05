namespace JustyBase.Editor.InlineCompletion;

/// <summary>
/// Debounced inline AI completion (ghost text + Tab accept) for AvaloniaEdit.
/// </summary>
public sealed class InlineCompletionController : IDisposable
{
    public const int DefaultDebounceMs = 600;
    public const int MinDebounceMs = 250;
    public const int MaxDebounceMs = 3000;

    public static IReadOnlyList<int> AllowedDebounceMs { get; } = [250, 400, 600, 1000, 2000, 3000];

    private readonly TextEditor _editor;
    private readonly CodeTextEditor? _completionHost;
    private readonly GhostTextElementGenerator _generator;
    private readonly Func<InlineCompletionContext, CancellationToken, Task<string?>> _completeAsync;
    private readonly Func<int>? _getDebounceMs;
    private readonly Func<bool>? _getIsEnabled;
    private readonly int _debounceMs;
    private CancellationTokenSource? _debounceCts;
    private bool _attached;
    private bool _disposed;
    private CompletionSelectionSnapshot? _completionSelection;
    private bool _completionAcceptancePending;
    private bool _completionContinuationActive;
    private int _ghostCompletionPrefixLength;

    public InlineCompletionController(
        TextEditor editor,
        Func<InlineCompletionContext, CancellationToken, Task<string?>> completeAsync,
        int debounceMs = DefaultDebounceMs,
        Func<int>? getDebounceMs = null,
        Func<bool>? getIsEnabled = null,
        CodeTextEditor? completionHost = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
        _getDebounceMs = getDebounceMs;
        _getIsEnabled = getIsEnabled;
        _completionHost = completionHost;
        _debounceMs = SnapDebounceMs(debounceMs);
        _generator = new GhostTextElementGenerator();
    }

    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _getIsEnabled?.Invoke() ?? _isEnabled;
        set => _isEnabled = value;
    }

    private int ResolveDebounceMs() =>
        SnapDebounceMs(_getDebounceMs?.Invoke() ?? _debounceMs);

    public static int SnapDebounceMs(int debounceMs)
    {
        if (debounceMs <= 0)
        {
            return DefaultDebounceMs;
        }

        var clamped = Math.Clamp(debounceMs, MinDebounceMs, MaxDebounceMs);
        var best = AllowedDebounceMs[0];
        var bestDist = Math.Abs(best - clamped);
        foreach (var option in AllowedDebounceMs)
        {
            var dist = Math.Abs(option - clamped);
            if (dist < bestDist)
            {
                best = option;
                bestDist = dist;
            }
        }

        return best;
    }

    /// <summary>Legacy helper: preference seconds → nearest allowed ms.</summary>
    public static int DebounceMsFromSeconds(int seconds) =>
        SnapDebounceMs(Math.Clamp(seconds, 1, 15) * 1000);

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_attached)
        {
            return;
        }

        _editor.TextArea.TextView.ElementGenerators.Add(_generator);
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.Caret.PositionChanged += OnCaretChanged;
        _editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        _editor.TextArea.KeyDown += OnKeyDown;
        _editor.Document.Changed += OnDocumentChanged;
        if (_completionHost is not null)
        {
            _completionHost.CompletionSelectionChanged += OnCompletionSelectionChanged;
            _completionHost.CompletionWindowClosed += OnCompletionWindowClosed;
        }
        _attached = true;
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        CancelPending();
        ClearGhostText();
        _editor.TextArea.TextEntering -= OnTextEntering;
        _editor.TextArea.TextEntered -= OnTextEntered;
        _editor.TextArea.Caret.PositionChanged -= OnCaretChanged;
        _editor.TextArea.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
        _editor.TextArea.KeyDown -= OnKeyDown;
        _editor.Document.Changed -= OnDocumentChanged;
        if (_completionHost is not null)
        {
            _completionHost.CompletionSelectionChanged -= OnCompletionSelectionChanged;
            _completionHost.CompletionWindowClosed -= OnCompletionWindowClosed;
        }
        _editor.TextArea.TextView.ElementGenerators.Remove(_generator);
        _attached = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();
        _debounceCts?.Dispose();
        _debounceCts = null;
        _disposed = true;
    }

    /// <summary>Requests a completion using the current editor text and caret position.</summary>
    public void RequestCompletion() => Schedule();

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        // CompletionWindow handles Tab as a key event. Keep the preview alive until
        // its insertion changes the document so the first Tab can leave the AI tail.
        if (_completionAcceptancePending && string.Equals(e.Text, "\t", StringComparison.Ordinal))
        {
            return;
        }

        _completionContinuationActive = false;
        if (_generator.HasGhostText)
        {
            ClearGhostText();
        }
    }

    private void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (_completionAcceptancePending)
        {
            return;
        }

        Schedule();
    }

    private void OnCaretChanged(object? sender, EventArgs e)
    {
        if (_generator.HasGhostText && _generator.Offset != _editor.CaretOffset)
        {
            ClearGhostText();
        }

        Schedule();
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_completionAcceptancePending && _completionSelection is not null)
        {
            var ghost = _generator.HasGhostText ? _generator.Text ?? string.Empty : string.Empty;
            var continuation = _ghostCompletionPrefixLength >= ghost.Length
                ? string.Empty
                : ghost[_ghostCompletionPrefixLength..];

            _completionAcceptancePending = false;
            _completionSelection = null;
            _completionContinuationActive = !string.IsNullOrEmpty(continuation);
            CancelPending();
            ClearGhostText();
            if (!string.IsNullOrEmpty(continuation))
            {
                SetGhostText(_editor.CaretOffset, continuation);
            }

            return;
        }

        if (_generator.HasGhostText)
        {
            ClearGhostText();
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Tab
            && e.KeyModifiers == KeyModifiers.None
            && _completionHost?.IsCompletionWindowOpen == true)
        {
            _completionAcceptancePending = _completionSelection is not null;
            return;
        }

        if (e.Key == Key.Tab
            && e.KeyModifiers == KeyModifiers.None
            && _generator.HasGhostText
            && !string.IsNullOrEmpty(_generator.Text))
        {
            AcceptGhostText();
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _generator.HasGhostText)
        {
            ClearGhostText();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Right or Key.Left or Key.Up or Key.Down)
        {
            if (_completionHost?.IsCompletionWindowOpen != true && _generator.HasGhostText)
            {
                ClearGhostText();
            }
        }
    }

    private void OnCompletionSelectionChanged(object? sender, CompletionSelectionChangedEventArgs e)
    {
        _completionAcceptancePending = false;
        _completionContinuationActive = false;
        _completionSelection = e.Selection;
        CancelPending();
        ClearGhostText();

        if (_completionSelection is not null)
        {
            var visibleSeed = GetVisibleCompletionText(_completionSelection);
            _ghostCompletionPrefixLength = visibleSeed.Length;
            if (!string.IsNullOrEmpty(visibleSeed))
            {
                SetGhostText(_editor.CaretOffset, visibleSeed);
            }
        }

        Schedule();
    }

    private void OnCompletionWindowClosed(object? sender, EventArgs e)
    {
        if (_completionAcceptancePending)
        {
            return;
        }

        if (_completionContinuationActive)
        {
            _completionContinuationActive = false;
            return;
        }

        _completionSelection = null;
        ClearGhostText();
    }

    private void AcceptGhostText()
    {
        if (!_generator.HasGhostText || string.IsNullOrEmpty(_generator.Text))
        {
            return;
        }

        var text = _generator.Text;
        var offset = _generator.Offset;
        ClearGhostText();
        CancelPending();
        _editor.Document.Insert(offset, text);
        _editor.CaretOffset = offset + text.Length;
    }

    private string GetVisibleCompletionText(CompletionSelectionSnapshot selection)
    {
        var start = Math.Clamp(selection.ReplacementStartOffset, 0, _editor.Document.TextLength);
        var end = Math.Clamp(selection.ReplacementEndOffset, start, _editor.Document.TextLength);
        var typed = _editor.Document.GetText(start, Math.Min(end, _editor.CaretOffset) - start);
        if (selection.InsertText.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            return selection.InsertText[typed.Length..];
        }

        return selection.InsertText;
    }

    private void Schedule()
    {
        if (!IsEnabled || _disposed)
        {
            return;
        }

        // Cancel previous debounce/in-flight completion — typing again resets the timer.
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = RunDebouncedAsync(token);
    }

    private async Task RunDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ResolveDebounceMs(), token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            string documentText = string.Empty;
            int caret = 0;
            CompletionSelectionSnapshot? completionSelection = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                documentText = _editor.Document.Text;
                caret = _editor.CaretOffset;
                completionSelection = _completionSelection;
            }).GetTask().ConfigureAwait(false);

            if (token.IsCancellationRequested || string.IsNullOrWhiteSpace(documentText))
            {
                return;
            }

            if (caret <= 0 && documentText.Length == 0)
            {
                return;
            }

            var suggestion = await _completeAsync(
                new InlineCompletionContext(documentText, caret, completionSelection),
                token).ConfigureAwait(false);

            if (token.IsCancellationRequested || string.IsNullOrEmpty(suggestion))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested
                    || _editor.CaretOffset != caret
                    || !Equals(_completionSelection, completionSelection))
                {
                    return;
                }

                var continuation = NormalizeContinuation(suggestion, completionSelection);
                var visibleSeed = completionSelection is null
                    ? string.Empty
                    : GetVisibleCompletionText(completionSelection);
                _ghostCompletionPrefixLength = visibleSeed.Length;
                SetGhostText(caret, visibleSeed + continuation);
            }).GetTask().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal when the user types again / caret moves / dispose.
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FIM] inline completion failed: {ex}");
        }
#pragma warning restore CA1031
    }

    private void CancelPending()
    {
        try
        {
            _debounceCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    private void ClearGhostText()
    {
        if (!_generator.HasGhostText)
        {
            _ghostCompletionPrefixLength = 0;
            return;
        }

        _generator.Clear();
        _ghostCompletionPrefixLength = 0;
        _editor.TextArea.TextView.Redraw();
    }

    private void SetGhostText(int offset, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            ClearGhostText();
            return;
        }

        _generator.Set(offset, text, _editor.FontFamily, _editor.FontSize);
        _editor.TextArea.TextView.Redraw();
    }

    private static string NormalizeContinuation(string suggestion, CompletionSelectionSnapshot? selection)
    {
        if (selection is null || string.IsNullOrEmpty(suggestion))
        {
            return suggestion;
        }

        var selectedText = selection.InsertText;
        return suggestion.StartsWith(selectedText, StringComparison.OrdinalIgnoreCase)
            ? suggestion[selectedText.Length..]
            : suggestion;
    }
}

public readonly record struct InlineCompletionContext(
    string DocumentText,
    int CaretOffset,
    CompletionSelectionSnapshot? CompletionSelection = null);
