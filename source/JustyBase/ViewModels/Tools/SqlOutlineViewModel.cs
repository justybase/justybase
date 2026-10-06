using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Services;
using JustyBase.ViewModels.Documents;

namespace JustyBase.ViewModels.Tools;

public sealed partial class SqlOutlineViewModel : Tool
{
    private readonly AvaloniaList<SqlOutlineItem> _items = new();
    private string? _lastBuiltSql;

    public SqlOutlineViewModel(IFactory factory)
    {
        this.Factory = factory;
    }

    public IReadOnlyList<SqlOutlineItem> Items => _items;

    /// <summary>
    /// Legacy navigation hook (wired by the linter to the last attached editor).
    /// Used only when the active document has no usable editor.
    /// </summary>
    public Action<int>? NavigateToOffset { get; set; }

    [ObservableProperty]
    public partial SqlOutlineItem? SelectedItem { get; set; }

    partial void OnSelectedItemChanged(SqlOutlineItem? value)
    {
        // Single click / keyboard navigation: move the caret, keep focus in the tree.
        // Programmatic selection from FollowCaret must not navigate back.
        if (value is not null && !_suppressNavigate)
            NavigateToItem(value, focusEditor: false);
    }

    private bool _suppressNavigate;

    /// <summary>
    /// VS Code "follow cursor": highlights the deepest symbol containing the
    /// caret offset. Never navigates by itself.
    /// </summary>
    public void FollowCaret(int caretOffset)
    {
        SqlOutlineItem? match = null;
        var stack = new Stack<SqlOutlineItem>(_items.Reverse());
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.StartOffset <= caretOffset)
                match = current;
            for (int i = current.Children.Count - 1; i >= 0; i--)
                stack.Push(current.Children[i]);
        }

        if (match is null || ReferenceEquals(match, SelectedItem))
            return;

        _suppressNavigate = true;
        try
        {
            SelectedItem = match;
        }
        finally
        {
            _suppressNavigate = false;
        }
    }

    /// <summary>
    /// VS Code style go-to-symbol: caret to the symbol start in the active
    /// document. Double-click / Enter additionally focuses the editor.
    /// </summary>
    public void NavigateToItem(SqlOutlineItem item, bool focusEditor)
    {
        if (!ReferenceEquals(SelectedItem, item))
            SelectedItem = item;

        if (Factory is IActiveDocumentManager documents
            && documents.ActiveSqlDocumentViewModel?.SqlEditor?.Document is { } textDocument
            && documents.ActiveSqlDocumentViewModel is SqlDocumentViewModel doc
            && doc.SqlEditor is { } editor)
        {
            int offset = Math.Clamp(item.StartOffset, 0, textDocument.TextLength);
            editor.CaretOffset = offset;
            editor.TextArea.Caret.BringCaretToView();
            if (focusEditor)
            {
                documents.FocusSqlDocument(doc);
                editor.Focus();
            }

            return;
        }

        NavigateToOffset?.Invoke(item.StartOffset);
    }

    [RelayCommand]
    private void GoToSelectedItem()
    {
        if (SelectedItem is not null)
            NavigateToItem(SelectedItem, focusEditor: true);
    }

    [RelayCommand]
    private void GoToItem(SqlOutlineItem? item)
    {
        if (item is not null)
            NavigateToItem(item, focusEditor: true);
    }

    public void UpdateOutline(string sql)
    {
        // Activations fire often (dialogs, tool focus); rebuilding parses the
        // whole script, so skip when the text did not change.
        if (string.Equals(sql, _lastBuiltSql, StringComparison.Ordinal))
            return;

        _lastBuiltSql = sql;
        _items.Clear();
        SelectedItem = null;
        SqlOutlineItem? parent = null;
        var line = 1;
        var lineOffset = 0;
        foreach (var entry in SqlOutlineBuilder.Build(sql))
        {
            var targetOffset = Math.Min(entry.StartOffset, sql.Length);
            while (lineOffset < targetOffset)
                if (sql[lineOffset++] == '\n') line++;
            var item = new SqlOutlineItem(entry.Title, entry.Kind, entry.StartOffset, line);
            if (entry.Depth == 0 || parent is null)
            {
                _items.Add(item);
                parent = item;
            }
            else
            {
                parent.Children.Add(item);
            }
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        _lastBuiltSql = null;
        if (GetCurrentSql is not null)
            UpdateOutline(GetCurrentSql());
    }

    public Func<string>? GetCurrentSql { get; set; }
}

public sealed class SqlOutlineItem(string title, string kind, int startOffset, int line)
{
    private static readonly Geometry StatementIcon = StreamGeometry.Parse(
        "M5 2h10l5 5v15H5z M14 4v4h4 M8 11h9v2H8z M8 15h9v2H8z");
    private static readonly Geometry CteIcon = StreamGeometry.Parse(
        "M3 3h7v7H3z M14 3h7v7h-7z M8 14h8v7H8z M10 5h4v2h-4z M11 10h2v4h-2z");
    // VS Code symbol colors: statements blue (like documents), CTEs purple (like methods).
    private static readonly IBrush StatementBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0x9A, 0xD1));
    private static readonly IBrush CteBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0x86, 0xC0));

    public string Title { get; } = title;
    public string Kind { get; } = kind;
    public int StartOffset { get; } = startOffset;
    public int Line { get; } = line;
    public Geometry IconData => Kind == "CTE" ? CteIcon : StatementIcon;
    public IBrush IconBrush => Kind == "CTE" ? CteBrush : StatementBrush;
    public AvaloniaList<SqlOutlineItem> Children { get; } = [];
}
