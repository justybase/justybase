using JustyBase.Helpers;
using JustyBase.Core.Database;
using JustyBase.Editor.CompletionProviders;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommons;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlParser.Authoring;


namespace JustyBase.Editor;

public sealed partial class SqlCodeEditor : CodeTextEditor
{
    private BraceMatcherHighlightRenderer? _braceMatcherHighlighter;
    private TextMarkerService _textMarkerService = null!;

    public SqlCodeEditor()
    {

        this.TextArea.Caret.PositionChanged += CaretOnPositionChanged;

        this.AddHandler(PointerWheelChangedEvent, (o, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                if (e.Delta.Y > 0 && FontSize < 60)
                {
                    FontSize += 1;
                }
                else if (FontSize > 3)
                {
                    FontSize -= 1;
                }
            }
        }, RoutingStrategies.Bubble, true);

        SetupCommandBindings();
    }
    private void SetupCommandBindings()
    {
        //
        var handler = (TextAreaDefaultInputHandler)TextArea.ActiveInputHandler;
        handler.Detach();
        //TODO selection up/down
        var lineUp = new RoutedCommand("LineUp", new KeyGesture(Key.Up, KeyModifiers.Control));
        var lineDown = new RoutedCommand("LineDown", new KeyGesture(Key.Down, KeyModifiers.Control));

        handler.CommandBindings.Add(new RoutedCommandBinding(lineUp, (o, e) =>
        {
            var currentLine = this.TextArea.Caret.Line;
            if (currentLine > 1)
            {
                DocumentLine line0 = this.Document.Lines[currentLine - 1];
                var line1 = this.Document.Lines[currentLine - 2];
                EditorHelpers.SwapLines(this, line0, line1);
            }
        }));

        handler.CommandBindings.Add(new RoutedCommandBinding(lineDown, (o, e) =>
        {
            var currentLine = this.TextArea.Caret.Line;
            if (currentLine < this.LineCount - 1)
            {
                var line0 = this.Document.Lines[currentLine - 1];
                var line1 = this.Document.Lines[currentLine + 1];
                EditorHelpers.SwapLines(this, line0, line1);
            }
        }));

        handler.Attach();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        _foldingTimer?.Stop();
        _foldingTimer?.Start();

        base.OnKeyDown(e);
        // Key.Oem7 = " or '
        if (TextArea.IsFocused && e.Key == Key.Oem7 && TextArea.Selection.Length < 1024)
        {
            int selectionLength = TextArea.Selection.Length;
            if (selectionLength == 0)
            {
                if (e.HasModifiers(ModifierKeys.Shift))
                {
                    TextArea.Document.Insert(CaretOffset, "\"");
                }
                else
                {
                    TextArea.Document.Insert(CaretOffset, "'");
                }
                TextArea.Caret.Offset--;
            }
            else
            {
                if (e.HasModifiers(ModifierKeys.Shift))
                {
                    TextArea.Selection.ReplaceSelectionWithText($"\"{TextArea.Selection.GetText()}");
                }
                else
                {
                    TextArea.Selection.ReplaceSelectionWithText($"'{TextArea.Selection.GetText()}");
                }
            }
        }
        // D9 = "("
        else if (TextArea.IsFocused && e.Key == Key.D9 && e.HasModifiers(ModifierKeys.Shift) && TextArea.Selection.Length < 1024)
        {
            var selection = TextArea.Selection;
            int sellen = selection.Length;
            if (sellen > 0 || selection is not RectangleSelection)
            {
                if (sellen == 0 && TextArea.Caret.Offset > 0 && selection is not RectangleSelection)
                {
                    selection.ReplaceSelectionWithText($"()");
                    //textArea.Document.Insert(CaretOffset, ")");
                    TextArea.Caret.Offset--;
                }
                else if (sellen > 0 && selection is RectangleSelection)
                {
                    selection.ReplaceSelectionWithText($"({selection.GetText().Replace("\r\n", ")\r\n(")})");
                    //_removeLastChar = true;
                }
                else if (selection is not RectangleSelection)
                {
                    selection.ReplaceSelectionWithText($"({selection.GetText()})");
                    //_removeLastChar = true;
                }
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Space && e.HasModifiers(ModifierKeys.Control))
        {
            e.Handled = true;
            var mode = e.HasModifiers(ModifierKeys.Shift)
                ? TriggerMode.SignatureHelp
                : TriggerMode.Completion;
            _ = ShowCompletion(mode);
        }
        else if (e.Key == Key.Space && e.HasModifiers(ModifierKeys.None))
        {
            _completionWindow?.Close();
            if (TextArea is not null)
            {
                ImmediateReplaceQuickOrTypo(e);
                //e.Handled = true;
            }
        }
        else if (e.Key == Key.C && e.HasModifiers(ModifierKeys.Alt)/*&& _completionWindow?.IsOpen() != true*/)
        {
            if (_completionWindow?.IsOpen() == true)
            {
                _completionWindow.Close();
            }
            if (TextArea is not null)
            {
                ImmediateReplaceStandard();
                //e.Handled = true;
            }
            //e.Handled = true;
        }
        else if (e.HasModifiers(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.H:
                    if (ForcedContolhtAction is not null)
                    {
                        ForcedContolhtAction.Invoke();
                        e.Handled = true;
                    }
                    break;
                case Key.F:
                    if (ForcedContolftAction is not null)
                    {
                        ForcedContolftAction.Invoke();
                        e.Handled = true;
                    }
                    break;
                case Key.D:
                    if (e.HasModifiers(ModifierKeys.Shift))
                    {
                        EditorHelpers.DoubleSelectedLine(this);
                    }
                    break;
                case Key.G:
                    if (GoToLineAsyncAction is null)
                    {
                        break;
                    }
                    int res = await GoToLineAsyncAction();
                    if (res > 0 && TextArea is not null)
                    {
                        TextArea.Caret.Line = res;
                        TextArea.Caret.BringCaretToView();
                    }
                    break;
                case Key.E:
                    ExpandFoldings();
                    break;
                case Key.R:
                    CollapseFoldings();
                    break;
                case Key.U:
                    if (e.HasModifiers(ModifierKeys.Shift))
                    {
                        SelectedText = SelectedText.ToLower();
                    }
                    else
                    {
                        SelectedText = SelectedText.ToUpper();
                        //    var HighlightDefinition = this.SyntaxHighlighting;
                        //    var Highlighter = new AvaloniaEdit.Highlighting.DocumentHighlighter(Document, HighlightDefinition);

                        //    AvaloniaEdit.Highlighting.HighlightedLine result = Highlighter.HighlightLine(0);

                        //    int off = 0;
                        //    bool isInComment = result.Sections.Any(
                        //s => s.Offset <= off && s.Offset + s.Length >= off
                        //     && s.Color.Name == "Comment");
                        //http://avalonedit.net/documentation/html/4d4ceb51-154d-43f0-b876-ad9640c5d2d8.htm ?
                        //var documentHighlighter = new AvaloniaEdit.Highlighting.DocumentHighlighter(this.Document, cachedHighlightingDefinition);
                        //var colorizer = TextArea.TextView.LineTransformers[0];
                        //var sp = documentHighlighter.GetSpanStack(5);
                        //bool isInComment = result.Sections.Any(
                        //    s => s.Offset <= off && s.Offset + s.Length >= off
                        //         && s.Color.Name == "Comment");
                    }
                    break;
                case Key.J:
                    if (e.HasModifiers(ModifierKeys.Shift))
                    {
                        SelectedText = SelectedText.ChangeCaseRespectingSqlRules(false);
                    }
                    else
                    {
                        SelectedText = SelectedText.ChangeCaseRespectingSqlRules(true);
                    }
                    break;
                case Key.V:
                    if (e.HasModifiers(ModifierKeys.Shift) && ContolShiftvAction is not null)
                    {
                        await ContolShiftvAction.Invoke();
                    }
                    break;
            }
        }
        else if (e.Key == Key.F2)
        {
            e.Handled = true;
            RenameRequested?.Invoke();
        }
        else if (e.Key == Key.F12)
        {
            if (e.HasModifiers(ModifierKeys.Shift))
            {
                e.Handled = true;
                FindReferencesRequested?.Invoke();
            }
            else
            {
                e.Handled = true;
                GoToDefinitionRequested?.Invoke();
            }
        }

        ///replace if typo or "quick snippet"
        void ImmediateReplaceQuickOrTypo(KeyEventArgs e)
        {
            int offset = TextArea.Caret.Offset;
            Span<char> chars = stackalloc char[LastWordLenLimit];
            int lastWordLength = EditorHelpers.GetLastWord(TextArea, chars);

            if (lastWordLength <= 8 && _someEditorOptions?.FastReplaceDictionary is not null)
            {
                string tmp = chars[..lastWordLength].ToString();
                if (_someEditorOptions.FastReplaceDictionary.TryGetValue(tmp, out var res))
                {
                    int ind = res.IndexOf("${Caret}");
                    if (ind > 0)
                    {
                        ind = res.Length - ind - "${Caret}".Length;
                        res = res.Replace("${Caret}", "");
                    }
                    TextArea.Document.Replace(offset - lastWordLength, lastWordLength, res);
                    if (ind > 0 && ind < TextArea.Caret.Offset)
                    {
                        TextArea.Caret.Offset -= ind;
                        e.Handled = true;
                    }
                }
            }
            if (lastWordLength >= 3 && lastWordLength < LastWordLenLimit && _someEditorOptions is not null)
            {
                var typoCandidate = chars[..lastWordLength];
                foreach (var correctWord in _someEditorOptions.TypoPatternList)
                {
                    int dist = typoCandidate.DamerauLevenshteinDistance(correctWord);
                    if (dist <= SqlCodeEditorHelpers.TypoLimit && dist >= 1)
                    {
                        TextArea.Document.Replace(offset - lastWordLength, lastWordLength, correctWord);
                    }
                }
            }
        }

        void ImmediateReplaceStandard()
        {
            int offset = TextArea.Caret.Offset;
            Span<char> chars = stackalloc char[LastWordLenLimit + 1];
            int lastWordLength = EditorHelpers.GetLastWord(TextArea, chars[1..]);

            if (lastWordLength > 0 && lastWordLength < LastWordLenLimit - 1)
            {
                chars[0] = '@';
                if (_someEditorOptions.GetAllSnippets.TryGetValue(new string(chars[..(lastWordLength + 1)]), out var res))
                {
                    TextArea.Document.Replace(offset - lastWordLength, lastWordLength, res.Text);
                }
            }
        }
    }
    private const int LastWordLenLimit = 32;

    private string LanguageFileExtension => this.Document.FileName is not null ? System.IO.Path.GetExtension(this.Document.FileName).ToLower() : "";

    private ISomeEditorOptions _someEditorOptions = null!;
    private InMemorySchemaProvider? _parserSchema;
    private DocumentParsingCoordinator? _parsingCoordinator;
    private string? _documentUri;
    private SqlDialect _documentDialect = SqlDialect.Netezza;
    private SqlCompletionProvider? _completionProvider;

    private bool _editorServicesInitialized;

    public void Initialize(ISqlAutocompleteData sqlAutocompleteData, ISomeEditorOptions someEditorOptions,
        NzCompletionEngine? completionEngine = null, InMemorySchemaProvider? parserSchema = null,
        DocumentParsingCoordinator? parsingCoordinator = null, string? documentUri = null,
        Action<string, string, string>? ensureTableColumns = null,
        SqlDialect dialect = SqlDialect.Netezza,
        ISqlDbWordListProvider? wordListProvider = null,
        Func<string?>? connectionNameProvider = null,
        Func<string?>? databaseNameProvider = null)
    {
        if (_editorServicesInitialized)
        {
            return;
        }

        _editorServicesInitialized = true;
        _someEditorOptions = someEditorOptions;
        _parserSchema = parserSchema;
        _parsingCoordinator = parsingCoordinator;
        _documentUri = documentUri;
        _documentDialect = dialect;
        _braceMatcherHighlighter = new BraceMatcherHighlightRenderer(TextArea.TextView);
        AsyncToolTipRequest = OnAsyncToolTipRequest;
        var completionProvider = new SqlCompletionProvider(this, sqlAutocompleteData, _someEditorOptions,
            completionEngine, parsingCoordinator, documentUri, ensureTableColumns, parserSchema, dialect,
            wordListProvider, connectionNameProvider, databaseNameProvider);
        _completionProvider = completionProvider;
        CompletionProvider = completionProvider;
        _textMarkerService = new TextMarkerService(this);
        TextArea.TextView.BackgroundRenderers.Add(_textMarkerService);
        TextArea.TextView.LineTransformers.Add(_textMarkerService);
        TextArea.TextView.LineTransformers.Add(new SemanticLineColorizer());
        Document.Changed += (_, _) => SemanticLineColorizer.ScheduleUpdate(Document, documentUri, TextArea.TextView, _documentDialect);
        SemanticLineColorizer.RegisterDocument(Document, documentUri, TextArea.TextView, _documentDialect);
        SemanticLineColorizer.ScheduleUpdate(Document, documentUri, TextArea.TextView, _documentDialect);
        var truncateLongLines = new TruncateLongLines();
        TextArea.TextView.ElementGenerators.Insert(0, truncateLongLines);
        //TextArea.TextView.ElementGenerators.Add(truncateLongLines);

        this.TextArea.SelectionChanged += TextArea_SelectionChanged;
        ContextRequested += OnContextRequestedQuickFixes;
        if (_someEditorOptions.CollapseFoldingOnStartup && ForceUpdateFoldings())
        {
            CollapseFoldings();
        }
    }

    private void OnContextRequestedQuickFixes(object? sender, ContextRequestedEventArgs e)
    {
        if (QuickFixMenuProvider is null || e.Handled) return;
        var offset = CaretOffset;
        var fixes = QuickFixMenuProvider(offset);
        if (fixes.Count == 0) return;

        var menu = new ContextMenu();
        foreach (var (header, apply) in fixes)
        {
            var item = new MenuItem { Header = "⚡ " + header };
            item.Click += (_, _) => apply();
            menu.Items.Add(item);
        }
        menu.Open(this);
        e.Handled = true;
    }
    private async Task OnAsyncToolTipRequest(ToolTipRequestEventArgs arg)
    {
        await Task.Delay(1);
        if (arg.Position < 0 || arg.Position >= Document.TextLength)
            return;

        try
        {
            var hover = NzHoverService.GetHover(
                Document.Text,
                arg.Position,
                _parserSchema,
                _parsingCoordinator,
                _documentUri,
                catalog: DialectRuntime.AuthoringCatalogOrNull(_documentDialect),
                dialect: _documentDialect);
            if (hover is not null)
            {
                arg.SetToolTip(hover.Content);
            }
        }
        catch
        {
            // Hover errors are non-fatal
        }
    }

    /// <summary>
    /// Switches the editor's SQL dialect (e.g. when the document's connection changes).
    /// Rebuilds the completion/hover authoring catalog and reclassifies semantic tokens.
    /// </summary>
    public void SetSqlDialect(SqlDialect dialect)
    {
        if (_documentDialect == dialect)
            return;

        _documentDialect = dialect;
        _completionProvider?.SetDialect(dialect);
        if (Document is not null)
        {
            SemanticLineColorizer.RegisterDocument(Document, _documentUri, TextArea?.TextView, dialect);
            SemanticLineColorizer.ScheduleUpdate(Document, _documentUri, TextArea?.TextView, dialect);
        }
    }


    public bool SelectError(int startOffset, int length, string? tooltip = null, object? tag = null)
    {
        var marker = _textMarkerService.TryCreate(startOffset, length);
        if (marker is not null)
        {
            marker.MarkerColor = Colors.Red;
            marker.ToolTip = tooltip ?? "Error";
            marker.Tag = tag;
            return true;
        }
        return false;
    }
    public bool SelectWarning(int startOffset, int length, string? tooltip = null, object? tag = null)
    {
        var marker = _textMarkerService.TryCreate(startOffset, length);
        if (marker is not null)
        {
            marker.MarkerColor = Colors.DarkOrange;
            marker.ToolTip = tooltip ?? "Warning";
            marker.Tag = tag;
            return true;
        }
        return false;
    }
    public void RemoveAllErrorsWarnings()
    {
        _textMarkerService.RemoveAll(marker => true);
    }

    /// <summary>
    /// Returns diagnostic markers overlapping the caret for context-menu quick fixes.
    /// </summary>
    public IEnumerable<TextMarker> GetDiagnosticMarkersAtOffset(int offset)
        => _textMarkerService.GetMarkersAtOffset(offset);

    /// <summary>
    /// Optional host hook: builds caret quick-fix menu items for the given offset.
    /// </summary>
    public Func<int, IReadOnlyList<(string Header, Action Apply)>>? QuickFixMenuProvider { get; set; }

    private void CaretOnPositionChanged(object? sender, EventArgs eventArgs)
    {
        SqlCodeEditorHelpers.LastFocusedEditor = this;
        if (_braceMatcherHighlighter is null || this.Document is null)
        {
            return;
        }

        int position = this.TextArea.Caret.Offset;
        if (position == 0)
        {
            return;
        }
        (int left, int right) = FindBrackets(position);

        if (left != -1 && right != -1)
        {
            _braceMatcherHighlighter.SetHighlight(new BraceMatchingResult(left, right), null);
        }
        else
        {
            _braceMatcherHighlighter.SetHighlight(null, null);
        }
    }

    private (int, int) FindBrackets(int caretOffset)
    {
        if (CleanSqlCode.Length != Document.TextLength)
        {
            CleanSqlCreator();
        }
        int left = FindLeftBracket(caretOffset);
        int right = FindRightBracket(caretOffset);

        if (left == -1 || right == -1)
        {
            return (-1, -1);
        }

        return (left, right);
    }

    private const int LEFT_BRACKET_BUFFER_LEN = 256;
    private int FindLeftBracket(int caretOffset)
    {
        int counter = 0;
        int maxIterations = SqlCodeEditorHelpers.BRACKET_SEARCH_LEN;

        if (caretOffset == 0)
        {
            return -1;
        }

        char c = default;

        do
        {
            ReadOnlySpan<char> spn2;
            int start = 0;
            if (caretOffset >= LEFT_BRACKET_BUFFER_LEN)
            {
                start = caretOffset - (LEFT_BRACKET_BUFFER_LEN - 1);
            }
            else
            {
                start = 0;
            }
            spn2 = CleanSqlCode.AsSpan(start, caretOffset - start);

            int index = spn2.LastIndexOfAny(SqlCodeEditorHelpers.leftBracket, SqlCodeEditorHelpers.rightBracket, ';');
            if (index < 0 && start == 0)
            {
                break;
            }
            else if (index < 0)
            {
                caretOffset -= (LEFT_BRACKET_BUFFER_LEN - 1);
                continue;
            }
            caretOffset = index + start;

            c = spn2[index];
            if (c == ';')
            {
                return -1;
            }

            if (c == SqlCodeEditorHelpers.leftBracket) counter++;
            if (c == SqlCodeEditorHelpers.rightBracket) counter--;
            if (counter == 1)
            {
                //found
                break;
            }
            //
            maxIterations--;
            if (maxIterations <= 0) break;
        } while (caretOffset > 1);

        if (c != SqlCodeEditorHelpers.leftBracket)
        {
            return -1;
        }

        return caretOffset;
    }
    private int FindRightBracket(int caretOffset)
    {
        int counter = 0;
        int maxIterations = SqlCodeEditorHelpers.BRACKET_SEARCH_LEN;
        //string characters = null;
        int docLen = Document.TextLength;

        if (caretOffset == docLen)
        {
            return -1;
        }
        --caretOffset;

        char c = default;
        do
        {
            var spn = CleanSqlCode.AsSpan(++caretOffset);
            int index = spn.IndexOfAny(SqlCodeEditorHelpers.leftBracket, SqlCodeEditorHelpers.rightBracket, ';');
            if (index < 0)
                break;
            maxIterations -= index;
            caretOffset += index;
            c = spn[index];

            if (c == ';')
            {
                return -1;
            }

            if (c == SqlCodeEditorHelpers.leftBracket) counter++;
            if (c == SqlCodeEditorHelpers.rightBracket) counter--;
            if (counter == -1)
            {
                //found
                break;
            }
            //

            if (maxIterations <= 0) break;
        } while (caretOffset < docLen - 1);

        if (c != SqlCodeEditorHelpers.rightBracket)
        {
            return -1;
        }

        return caretOffset;
    }

    private void TextArea_SelectionChanged(object? sender, EventArgs e)
    {
        foreach (var markSameWord in this.TextArea.TextView.LineTransformers.OfType<MarkSameWord>().ToList())
        {
            this.TextArea.TextView.LineTransformers.Remove(markSameWord);
        }

        if (!string.IsNullOrWhiteSpace(this.SelectedText) && this.SelectedText.Length < 512)
        {
            this.TextArea.TextView.LineTransformers.Add(new MarkSameWord(this.SelectedText));
        }
    }

    public Func<Task<int>> GoToLineAsyncAction { get; set; } = () => Task.FromResult(0);

    public Func<Task> ContolShiftvAction { get; set; } = () => Task.CompletedTask;

    public Action ForcedContolftAction { get; set; } = () => { };
    public Action ForcedContolhtAction { get; set; } = () => { };
    public Action RenameRequested { get; set; } = () => { };
    public Action GoToDefinitionRequested { get; set; } = () => { };
    public Action FindReferencesRequested { get; set; } = () => { };

}
