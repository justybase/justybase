using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using JustyBase.Editor;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Views.Documents;

public partial class GitDiffDocumentView : UserControl
{
    private bool _syncingScroll;
    private DiffLineBackgroundRenderer? _oldRenderer;
    private DiffLineBackgroundRenderer? _newRenderer;
    private int _appliedVersion = -1;
    private ScrollViewer? _oldScrollViewer;
    private ScrollViewer? _newScrollViewer;

    public GitDiffDocumentView()
    {
        InitializeComponent();
        ApplyDefaultSqlHighlighting(OldEditor);
        ApplyDefaultSqlHighlighting(NewEditor);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        WireScrollSync();
    }

    private void WireScrollSync()
    {
        UnwireScrollSync();

        _oldScrollViewer = FindEditorScrollViewer(OldEditor);
        _newScrollViewer = FindEditorScrollViewer(NewEditor);

        if (_oldScrollViewer is not null)
            _oldScrollViewer.ScrollChanged += OnOldScrollChanged;
        if (_newScrollViewer is not null)
            _newScrollViewer.ScrollChanged += OnNewScrollChanged;
    }

    private void UnwireScrollSync()
    {
        if (_oldScrollViewer is not null)
            _oldScrollViewer.ScrollChanged -= OnOldScrollChanged;
        if (_newScrollViewer is not null)
            _newScrollViewer.ScrollChanged -= OnNewScrollChanged;
        _oldScrollViewer = null;
        _newScrollViewer = null;
    }

    private static ScrollViewer? FindEditorScrollViewer(TextEditor editor) =>
        editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
        ?? editor.FindDescendantOfType<ScrollViewer>();

    private static void ApplyDefaultSqlHighlighting(TextEditor editor)
    {
        editor.SyntaxHighlighting =
            HighlightingManager.Instance.GetDefinition("SQL")
            ?? HighlightingManager.Instance.GetDefinition("GeneralSql");
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is GitDiffDocumentViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyContents(vm);
            // Template may materialize after first content apply.
            Dispatcher.UIThread.Post(WireScrollSync, DispatcherPriority.Loaded);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not GitDiffDocumentViewModel vm)
            return;
        if (e.PropertyName is nameof(GitDiffDocumentViewModel.ContentVersion)
            or nameof(GitDiffDocumentViewModel.OldDisplayText)
            or nameof(GitDiffDocumentViewModel.NewDisplayText)
            or nameof(GitDiffDocumentViewModel.Title))
        {
            ApplyContents(vm);
        }
        else if (e.PropertyName is nameof(GitDiffDocumentViewModel.CurrentChangeIndex))
        {
            ScrollToChange(vm);
        }
    }

    private void ApplyContents(GitDiffDocumentViewModel vm)
    {
        if (vm.ContentVersion == _appliedVersion)
            return;
        _appliedVersion = vm.ContentVersion;

        ApplyHighlightingForTitle(vm.Title);

        OldEditor.Text = vm.OldDisplayText;
        NewEditor.Text = vm.NewDisplayText;

        ReplaceRenderer(OldEditor, ref _oldRenderer, new DiffLineBackgroundRenderer(vm.OldLineKinds));
        ReplaceRenderer(NewEditor, ref _newRenderer, new DiffLineBackgroundRenderer(vm.NewLineKinds));

        Dispatcher.UIThread.Post(WireScrollSync, DispatcherPriority.Loaded);

        // Scroll to the first change even if CurrentChangeIndex is unchanged (e.g. re-diffing
        // over the current diff) — the PropertyChanged-based path only fires on actual changes.
        ScrollToChange(vm);
    }

    private void ApplyHighlightingForTitle(string? title)
    {
        // Titles look like "diff: SX5.sql @ abc1234"
        string name = title ?? string.Empty;
        int colon = name.IndexOf(':');
        if (colon >= 0 && colon + 1 < name.Length)
            name = name[(colon + 1)..].Trim();
        int at = name.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0)
            name = name[..at].Trim();

        string ext = Path.GetExtension(name);
        IHighlightingDefinition? highlighting;
        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            highlighting = HighlightingManager.Instance.GetDefinition(ext.TrimStart('.').ToUpperInvariant())
                ?? HighlightingManager.Instance.GetDefinition("TXT");
        }
        else
        {
            highlighting = HighlightingManager.Instance.GetDefinition("SQL")
                ?? HighlightingManager.Instance.GetDefinition("GeneralSql");
        }

        OldEditor.SyntaxHighlighting = highlighting;
        NewEditor.SyntaxHighlighting = highlighting;
    }

    private static void ReplaceRenderer(TextEditor editor, ref DiffLineBackgroundRenderer? field, DiffLineBackgroundRenderer next)
    {
        TextView view = editor.TextArea.TextView;
        if (field is not null)
            view.BackgroundRenderers.Remove(field);
        field = next;
        view.BackgroundRenderers.Add(field);
        view.InvalidateLayer(KnownLayer.Background);
    }

    private void ScrollToChange(GitDiffDocumentViewModel vm)
    {
        int line = vm.GetCurrentScrollTarget();
        if (line < 1)
            return;

        ScrollToLine(OldEditor, _oldScrollViewer, line);
        ScrollToLine(NewEditor, _newScrollViewer, line);
    }

    private static void ScrollToLine(TextEditor editor, ScrollViewer? scrollViewer, int line)
    {
        if (editor.Document is null || line < 1 || line > editor.Document.LineCount)
            return;

        if (scrollViewer is null)
            return;

        int totalLines = editor.Document.LineCount;
        if (totalLines <= 0)
            return;

        double targetRatio = (double)(line - 1) / totalLines;
        double targetY = targetRatio * scrollViewer.Extent.Height;
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Max(0, targetY));
    }

    private void OnOldScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll || _newScrollViewer is null || _oldScrollViewer is null)
            return;
        if (e.ExtentDelta == default && e.OffsetDelta == default && e.ViewportDelta == default)
            return;

        _syncingScroll = true;
        try
        {
            _newScrollViewer.Offset = _oldScrollViewer.Offset;
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private void OnNewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll || _oldScrollViewer is null || _newScrollViewer is null)
            return;
        if (e.ExtentDelta == default && e.OffsetDelta == default && e.ViewportDelta == default)
            return;

        _syncingScroll = true;
        try
        {
            _oldScrollViewer.Offset = _newScrollViewer.Offset;
        }
        finally
        {
            _syncingScroll = false;
        }
    }
}
