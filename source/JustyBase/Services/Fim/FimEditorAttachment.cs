using Avalonia.Threading;
using AvaloniaEdit;
using JustyBase.Editor;
using JustyBase.Editor.InlineCompletion;

namespace JustyBase.Services.Fim;

/// <summary>Attaches / detaches <see cref="InlineCompletionController"/> for a SQL editor.</summary>
public sealed class FimEditorAttachment : IDisposable
{
    private readonly FimInlineCompletionBridge? _bridge;
    private InlineCompletionController? _controller;
    private TextEditor? _editor;

    public FimEditorAttachment(FimInlineCompletionBridge? bridge)
    {
        _bridge = bridge;
        if (_bridge is not null)
        {
            _bridge.ModelReady += OnModelReady;
        }
    }

    public void Attach(
        TextEditor editor,
        Func<bool> isEnabled,
        Func<int>? getDebounceMs = null,
        Func<string, int, string?>? schemaHintProvider = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(isEnabled);
        Detach();
        if (_bridge is null)
        {
            return;
        }

        _editor = editor;
        _controller = new InlineCompletionController(
            editor,
            (ctx, ct) => _bridge.CompleteAsync(ctx, ct, schemaHintProvider),
            getDebounceMs: getDebounceMs,
            getIsEnabled: isEnabled,
            completionHost: editor as CodeTextEditor);
        _controller.Attach();
    }

    public void SyncEnabled(bool enabled)
    {
        if (_controller is not null)
        {
            _controller.IsEnabled = enabled;
        }
    }

    public void Detach()
    {
        _controller?.Dispose();
        _controller = null;
        _editor = null;
    }

    private void OnModelReady(object? sender, EventArgs e)
    {
        // ModelReady is raised from a background continuation (EnsureReadyAsync), so the
        // editor (an Avalonia control) must only be touched on the UI thread. Also only
        // nudge the focused editor — do not fan a completion request into every open
        // SQL document (unfocused editors stay quiet).
        if (_controller is null || _editor is null)
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_controller is null || _editor?.TextArea?.IsFocused != true)
            {
                return;
            }

            _controller.RequestCompletion();
        });
    }

    public void Dispose()
    {
        Detach();
        if (_bridge is not null)
        {
            _bridge.ModelReady -= OnModelReady;
        }
    }
}
