using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

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
    private readonly GhostTextElementGenerator _generator;
    private readonly Func<InlineCompletionContext, CancellationToken, Task<string?>> _completeAsync;
    private readonly Func<int>? _getDebounceMs;
    private readonly int _debounceMs;
    private CancellationTokenSource? _debounceCts;
    private bool _attached;
    private bool _disposed;

    public InlineCompletionController(
        TextEditor editor,
        Func<InlineCompletionContext, CancellationToken, Task<string?>> completeAsync,
        int debounceMs = DefaultDebounceMs,
        Func<int>? getDebounceMs = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
        _getDebounceMs = getDebounceMs;
        _debounceMs = SnapDebounceMs(debounceMs);
        _generator = new GhostTextElementGenerator();
    }

    public bool IsEnabled { get; set; } = true;

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

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (_generator.HasGhostText)
        {
            ClearGhostText();
        }
    }

    private void OnTextEntered(object? sender, TextCompositionEventArgs e) => Schedule();

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
            if (_generator.HasGhostText)
            {
                ClearGhostText();
            }
        }
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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                documentText = _editor.Document.Text;
                caret = _editor.CaretOffset;
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
                new InlineCompletionContext(documentText, caret),
                token).ConfigureAwait(false);

            if (token.IsCancellationRequested || string.IsNullOrEmpty(suggestion))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _editor.CaretOffset != caret)
                {
                    return;
                }

                _generator.Set(caret, suggestion, _editor.FontFamily, _editor.FontSize);
                _editor.TextArea.TextView.Redraw();
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
            return;
        }

        _generator.Clear();
        _editor.TextArea.TextView.Redraw();
    }
}

public readonly record struct InlineCompletionContext(string DocumentText, int CaretOffset);
