using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;

namespace JustyBase.Editor.InlineCompletion;

/// <summary>
/// Renders gray, semi-transparent ghost text at a document offset without consuming document characters.
/// </summary>
public sealed class GhostTextElementGenerator : VisualLineElementGenerator
{
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.FromArgb(150, 110, 110, 110));

    private string? _text;
    private int _offset = -1;
    private FontFamily? _fontFamily;
    private double _fontSize;

    public bool HasGhostText => !string.IsNullOrEmpty(_text) && _offset >= 0;

    public string? Text => _text;
    public int Offset => _offset;

    public void Set(int offset, string? text, FontFamily? fontFamily = null, double fontSize = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            Clear();
            return;
        }

        _offset = offset;
        _text = text;
        _fontFamily = fontFamily;
        _fontSize = fontSize > 0 ? fontSize : 0;
    }

    public void Clear()
    {
        _offset = -1;
        _text = null;
        _fontFamily = null;
        _fontSize = 0;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HasGhostText || _offset < startOffset)
        {
            return -1;
        }

        return _offset;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        if (!HasGhostText || offset != _offset || _text is null)
        {
            return null;
        }

        try
        {
            // FormattedTextElement(FormattedText) is broken in AvaloniaEdit: CreateTextRun reads Text (null).
            // TextRunProperties is also null until TextView assigns it — build brush at CreateTextRun time.
            return new GhostVisualLineElement(_text, GhostBrush, _fontFamily, _fontSize);
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            Clear();
            return null;
        }
    }
}

/// <summary>
/// Zero-document-length inline run with an explicit gray brush (properties built at CreateTextRun time).
/// </summary>
internal sealed class GhostVisualLineElement : VisualLineElement
{
    private readonly string _text;
    private readonly IBrush _foreground;
    private readonly FontFamily? _fontFamily;
    private readonly double _fontSize;

    public GhostVisualLineElement(string text, IBrush foreground, FontFamily? fontFamily, double fontSize)
        : base(visualLength: Math.Max(1, text.Length), documentLength: 0)
    {
        _text = text;
        _foreground = foreground;
        _fontFamily = fontFamily;
        _fontSize = fontSize;
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        var props = new VisualLineElementTextRunProperties(context.GlobalTextRunProperties);
        props.SetForegroundBrush(_foreground);
        if (_fontFamily is not null)
        {
            props.SetTypeface(new Typeface(_fontFamily));
        }

        if (_fontSize > 0)
        {
            props.SetFontRenderingEmSize(_fontSize);
        }

        var relativeOffset = Math.Max(0, startVisualColumn - VisualColumn);
        if (relativeOffset >= _text.Length)
        {
            return new TextCharacters(ReadOnlyMemory<char>.Empty, props);
        }

        return new TextCharacters(_text.AsMemory(relativeOffset), props);
    }
}
