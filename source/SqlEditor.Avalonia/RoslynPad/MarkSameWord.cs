namespace JustyBase.Editor;

public sealed class MarkSameWord : DocumentColorizingTransformer
{
    private readonly string _selectedText;

    public MarkSameWord(string selectedText)
    {
        _selectedText = selectedText;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (string.IsNullOrEmpty(_selectedText))
        {
            return;
        }

        int lineStartOffset = line.Offset;
        int lineLen = line.Length;
        var fullText = CurrentContext.Document.Text;
        var lineSpan = fullText.AsSpan(lineStartOffset, lineLen);
        int searchStart = 0;
        int index;
        while ((index = lineSpan.Slice(searchStart).IndexOf(_selectedText.AsSpan(), StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int actualIndex = searchStart + index;
            ChangeLinePart(
                lineStartOffset + actualIndex,
                lineStartOffset + actualIndex + _selectedText.Length,
                element => element.BackgroundBrush = Brushes.Gray
                );
            searchStart = actualIndex + 1;
            if (searchStart >= lineLen)
                break;
        }
    }
}