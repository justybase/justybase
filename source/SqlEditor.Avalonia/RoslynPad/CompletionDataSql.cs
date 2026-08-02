using System;
using JustyBase.Editor.CompletionProviders;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Editor;

public sealed class CompletionDataSql(
    string text,
    string desc,
    bool isSelected,
    Glyph glyph,
    SnippetManager? snippetManager,
    string? insertText = null,
    string? detailText = null,
    string? descriptionText = null)
    : ICompletionDataEx
{
    private readonly SnippetManager? _snippetManager = snippetManager;
    private readonly string _insertText = insertText ?? text;

    private readonly Glyph _glyph = glyph;

    public bool IsSelected { get; } = isSelected;

    public string SortText => throw new NotImplementedException();

    public string Text { get; } = text;

    public string InsertText => _insertText;

    public object Content => Text;

    /// <summary>Right-hand metadata column (data type, Table/View, kind).</summary>
    public string? DetailText { get; } = detailText;

    /// <summary>Optional description between label and detail (column/table comment).</summary>
    public string? DescriptionText { get; } = descriptionText;

    public object Description { get; } = BuildTip(desc, detailText, descriptionText);

    public double Priority { get; } = 0;

    public bool AutocompleteOnReturn { get; set; }

    public CommonImage Image => _glyph.ToImageSource()!;

    public static CompletionDataSql FromEngineItem(CompletionItem item, Glyph glyph)
    {
        var detailText = MapDetailText(item);
        var descriptionText = item.Documentation;
        return new CompletionDataSql(
            item.Label,
            detailText ?? item.Kind.ToString(),
            false,
            glyph,
            null,
            item.InsertText ?? item.Label,
            detailText,
            descriptionText);
    }

    public static string? MapDetailText(CompletionItem item) => item.Kind switch
    {
        CompletionKind.Table => "Table",
        CompletionKind.View => "View",
        CompletionKind.ExternalTable => "External",
        CompletionKind.Column => item.Detail ?? "Column",
        CompletionKind.Schema => "Schema",
        CompletionKind.Database => "Database",
        CompletionKind.Cte => "CTE",
        CompletionKind.Alias => "Alias",
        CompletionKind.Function => item.Detail ?? "Function",
        CompletionKind.Keyword => "Keyword",
        CompletionKind.Snippet => "Snippet",
        CompletionKind.Variable => "Variable",
        CompletionKind.DataType => "Type",
        _ => item.Detail ?? item.Kind.ToString()
    };

    private static string BuildTip(string fallbackDesc, string? detailText, string? descriptionText)
    {
        if (detailText is null && descriptionText is null)
            return fallbackDesc;

        if (string.IsNullOrWhiteSpace(descriptionText))
            return string.IsNullOrWhiteSpace(detailText) ? fallbackDesc : detailText!;

        if (string.IsNullOrWhiteSpace(detailText))
            return descriptionText!;

        return $"{detailText}\n{descriptionText}";
    }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        if (_glyph == Glyph.Snippet && CompleteSnippet(textArea, completionSegment, e))
        {
            return;
        }

        if (e is CommonTextEventArgs inputEventArgs && inputEventArgs.Text?.Length > 0)
        {
            return;
        }
        if (e is KeyEventArgs kea && kea?.Key == Key.Return && !AutocompleteOnReturn)
        {
            return;
        }

        textArea.Document.Replace(completionSegment, _insertText);
    }

    private bool CompletSnippetOnEnter(EventArgs e)
    {
        return AutocompleteOnReturn && e is KeyEventArgs keyEventArgs && keyEventArgs.Key == Key.Return;
    }

    private bool CompleteSnippet(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        char? completionChar = null;
        var txea = e as CommonTextEventArgs;
        if (txea != null && txea.Text?.Length > 0)
            completionChar = txea.Text[0];
        else if (e is KeyEventArgs kea && kea.Key == Key.Tab)
            completionChar = '\t';

        if (completionChar == '\t' || CompletSnippetOnEnter(e))
        {
            var snippet = _snippetManager?.FindSnippet(Text);
            if (snippet != null)
            {
                var editorSnippet = snippet.CreateAvalonEditSnippet();
                using (textArea.Document.RunUpdate())
                {
                    int tmpOffset = completionSegment.Offset;
                    int tmpLength = completionSegment.Length;
                    if (tmpOffset >= 1 && textArea.Document.GetCharAt(tmpOffset - 1) == '@')
                    {
                        tmpOffset--;
                        tmpLength++;
                    }

                    textArea.Document.Remove(tmpOffset, tmpLength);
                    editorSnippet.Insert(textArea);
                }
                if (txea != null)
                {
                    txea.Handled = true;
                }

                return true;
            }
        }

        return false;
    }
}
