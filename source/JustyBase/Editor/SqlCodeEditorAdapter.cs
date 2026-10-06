using JustyBase.Helpers;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Editor;

/// <summary>
/// F2: Avalonia-backed <see cref="IEditorAdapter"/>.
/// Thin wrapper — no logic, only delegation so VMs stay UI-free.
/// Created by the view when the concrete <c>SqlCodeEditor</c> is available.
/// </summary>
public sealed class SqlCodeEditorAdapter : IEditorAdapter
{
    private readonly SqlCodeEditor _editor;

    public SqlCodeEditorAdapter(SqlCodeEditor editor)
    {
        _editor = editor;
    }

    public SqlCodeEditor Inner => _editor;

    public string Text { get => _editor.Text ?? string.Empty; set => _editor.Text = value; }
    public string SelectedText { get => _editor.SelectedText ?? string.Empty; set => _editor.SelectedText = value; }
    public int SelectionStart => _editor.SelectionStart;
    public int SelectionLength => _editor.SelectionLength;
    public int CaretOffset { get => _editor.CaretOffset; set => _editor.CaretOffset = value; }
    public int LineCount => _editor.Document?.LineCount ?? 0;
    public bool IsReadOnly { get => _editor.IsReadOnly; set => _editor.IsReadOnly = value; }

    public void Cut() => _editor.Cut();
    public void Copy() => _editor.Copy();
    public void Paste() => _editor.Paste();
    public void Undo() => _editor.Undo();
    public void Redo() => _editor.Redo();
    public void Focus() => _editor.Focus();
    public void SelectAll() => _editor.SelectAll();
    public void Select(int startOffset, int length) => _editor.Select(startOffset, length);
    public void Insert(int offset, string text) => _editor.Document?.Insert(offset, text);
    public void Replace(int offset, int length, string text) => _editor.Document?.Replace(offset, length, text);
    public string GetTappedWord() => EditorHelpers.GetTappedWord(_editor);
    public string GetCaretInfo()
    {
        var c = _editor.TextArea?.Caret;
        return c is null ? string.Empty : $"offset {c.Offset:N0} column {c.Column} line {c.Line}  ";
    }
    public void SelectError(int position, int length) => _editor.SelectError(position, length);
    public void SetSqlDialect(SqlDialect dialect) => _editor.SetSqlDialect(dialect);
    public void InsertTextToPrevLineAndSelect(string text) => EditorHelpers.InsertTextToPrevLineAndSelect(_editor, text);
    public void ReplaceVariable() => EditorHelpers.ReplaceVariable(_editor);
}
