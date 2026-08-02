namespace JustyBase.Editor;

public static class AvaloniaEditExtensions
{
    public static bool IsOpen(this CompletionWindowBase window) => window?.IsEffectivelyVisible == true;

    public static void MakeSimillar(SqlCodeEditor source, SqlCodeEditor desitnation)
    {
        if (source.SyntaxHighlighting is not null)
        {
            desitnation.SyntaxHighlighting = source.SyntaxHighlighting;
        }

        desitnation.Document.Text = source.Text;
        desitnation.TextArea.Caret.Line = source.TextArea.Caret.Line;
        desitnation.TextArea.Caret.Column = source.TextArea.Caret.Column;
        desitnation.TextArea.Caret.Offset = source.TextArea.Caret.Offset;

        desitnation.TextArea.Caret.BringCaretToView();
        desitnation.SelectionStart = source.SelectionStart;
        desitnation.SelectionLength = source.SelectionLength;
    }
}
