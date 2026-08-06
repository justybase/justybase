using AvaloniaEdit;
using JustyBase.Editor;
using JustyBase.Editor.InlineCompletion;

namespace JustyBase.Services.Fim;

/// <summary>Attaches / detaches <see cref="InlineCompletionController"/> for a SQL editor.</summary>
public sealed class FimEditorAttachment : IDisposable
{
    private readonly FimInlineCompletionBridge? _bridge;
    private InlineCompletionController? _controller;

    public FimEditorAttachment(FimInlineCompletionBridge? bridge)
    {
        _bridge = bridge;
        if (_bridge is not null)
        {
            _bridge.ModelReady += OnModelReady;
        }
    }

    public void Attach(TextEditor editor, Func<bool> isEnabled, Func<int>? getDebounceMs = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(isEnabled);
        Detach();
        if (_bridge is null)
        {
            return;
        }

        _controller = new InlineCompletionController(
            editor,
            (ctx, ct) => _bridge.CompleteAsync(ctx, ct),
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
    }

    private void OnModelReady(object? sender, EventArgs e) => _controller?.RequestCompletion();

    public void Dispose()
    {
        Detach();
        if (_bridge is not null)
        {
            _bridge.ModelReady -= OnModelReady;
        }
    }
}
