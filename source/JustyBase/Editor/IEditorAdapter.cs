namespace JustyBase.Editor;

/// <summary>
/// F2: UI-free facade over <c>SqlCodeEditor</c> (AvaloniaEdit).
/// ViewModels depend on this interface instead of the concrete
/// <c>SqlCodeEditor</c> control so document logic is unit-testable
/// without Avalonia UI.
/// </summary>
public interface IEditorAdapter
{
    string Text { get; set; }
    string SelectedText { get; set; }
    int SelectionStart { get; }
    int SelectionLength { get; }
    int CaretOffset { get; set; }
    int LineCount { get; }
    bool IsReadOnly { get; set; }

    void Cut();
    void Copy();
    void Paste();
    void Undo();
    void Redo();
    void Focus();
    void SelectAll();
    void Select(int startOffset, int length);
    void Insert(int offset, string text);
    void Replace(int offset, int length, string text);
    string GetTappedWord();
    string GetCaretInfo();
    void SelectError(int position, int length);
    void SetSqlDialect(JustyBase.NetezzaSqlParser.Dialects.SqlDialect dialect);
    void InsertTextToPrevLineAndSelect(string text);
    void ReplaceVariable();
}
