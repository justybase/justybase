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
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;


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

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#';

    private string? ResolveTokenTip(Token<NzToken> token, Token<NzToken>[] allTokens, int index, string word)
    {
        // Keywords with descriptions
        var kwTip = GetKeywordTooltip(token.Kind, word);
        if (kwTip is not null) return kwTip;

        if (token.Kind != NzToken.Identifier && token.Kind != NzToken.QuotedIdentifier)
            return null;

        // Data types
        if (IsDataType(word))
            return GetDataTypeTip(word);

        // Boolean literals
        if (string.Equals(word, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(word, "FALSE", StringComparison.OrdinalIgnoreCase))
            return $"{word.ToUpperInvariant()} — boolean literal";

        // Column reference: table.column or alias.column
        bool isColumnRef = index >= 2 &&
            allTokens[index - 1].Kind == NzToken.Dot &&
            allTokens[index - 2].Kind == NzToken.Identifier;

        if (isColumnRef)
        {
            var tblName = allTokens[index - 2].ToStringValue();
            return ResolveColumnTip(tblName, word);
        }

        // Function call: ident + (
        bool isFunction = index + 1 < allTokens.Length &&
            allTokens[index + 1].Kind == NzToken.LParen;

        if (isFunction)
            return GetFunctionTip(word);

        // Known function without parens
        if (IsKnownFunction(word))
            return GetFunctionTip(word);

        // Table name — lookup in schema
        if (_parserSchema is not null)
        {
            var info = _parserSchema.GetTable(null, null, word);
            if (info?.Columns is not null && info.Columns.Count > 0)
            {
                var lines = new List<string> { $"**{word}** — table" };
                foreach (var col in info.Columns)
                    lines.Add($"- `{col.Name}`");
                return string.Join("\n", lines);
            }
        }

        // CTE name — scan token stream for WITH name AS (...)
        var cteTip = ResolveCteTip(allTokens, word);
        if (cteTip is not null) return cteTip;

        // Table alias — resolve alias to table name, show columns
        var aliasTip = ResolveAliasTip(allTokens, index, word);
        if (aliasTip is not null) return aliasTip;

        return null;
    }

    private string? ResolveCteTip(Token<NzToken>[] tokens, string word)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind == NzToken.With && i + 1 < tokens.Length)
            {
                int j = i + 1;
                if (j < tokens.Length && tokens[j].Kind == NzToken.Recursive)
                    j++;

                while (j < tokens.Length && tokens[j].Kind == NzToken.Identifier)
                {
                    var cteName = tokens[j].ToStringValue();
                    if (string.Equals(cteName, word, StringComparison.OrdinalIgnoreCase))
                    {
                        // Found it — extract columns from the SELECT list
                        var columns = ExtractCteColumnsForHover(tokens, j);
                        var lines = new List<string> { $"**{word}** — Common Table Expression (CTE)" };
                        if (columns.Count > 0)
                        {
                            lines.Add("");
                            lines.Add("Columns:");
                            foreach (var col in columns)
                                lines.Add($"- `{col}`");
                        }
                        return string.Join("\n", lines);
                    }

                    // Skip past CTE definition
                    j++;
                    if (j < tokens.Length && tokens[j].Kind == NzToken.LParen
                        && IsCteColumnListStart(tokens, j))
                        SkipBalancedParens(tokens, ref j);
                    if (j < tokens.Length && tokens[j].Kind == NzToken.As)
                    {
                        j++;
                        // Skip over CTE body (balanced parens)
                        if (j < tokens.Length && tokens[j].Kind == NzToken.LParen)
                        {
                            var saved = j;
                            SkipBalancedParens(tokens, ref j);
                            // Nested WITH inside: skip additional paren bodies
                            while (j < tokens.Length && tokens[j].Kind == NzToken.LParen
                                && AllocBalancedParensSkip(tokens, ref j))
                            {
                            }
                        }
                        if (j < tokens.Length && tokens[j].Kind == NzToken.Comma)
                            j++;
                        else
                            break;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        return null;
    }

    private static List<string> ExtractCteColumnsForHover(Token<NzToken>[] tokens, int cteNamePos)
    {
        // Find AS + LParen, then scan for SELECT item names within the body
        int j = cteNamePos + 1;
        if (j < tokens.Length && tokens[j].Kind == NzToken.LParen && IsCteColumnListStart(tokens, j))
        {
            // Explicit column list: cte (col1, col2) AS ...
            var cols = new List<string>();
            j++;
            while (j < tokens.Length && tokens[j].Kind == NzToken.Identifier)
            {
                cols.Add(tokens[j].ToStringValue());
                j++;
                if (j < tokens.Length && tokens[j].Kind == NzToken.Comma)
                    j++;
                else if (j < tokens.Length && tokens[j].Kind == NzToken.RParen)
                    return cols;
                else
                    break;
            }
        }

        // Find AS keyword
        while (j < tokens.Length && tokens[j].Kind != NzToken.As)
            j++;
        if (j >= tokens.Length) return new List<string>();
        j++; // past AS

        // Skip optional ALL
        if (j < tokens.Length && tokens[j].Kind == NzToken.All)
            j++;

        // Find body LParen
        while (j < tokens.Length && tokens[j].Kind != NzToken.LParen)
            j++;
        if (j >= tokens.Length) return new List<string>();

        // Find matching RParen
        int bodyStart = j;
        int depth = 1;
        j++;
        while (j < tokens.Length && depth > 0)
        {
            if (tokens[j].Kind == NzToken.LParen) depth++;
            else if (tokens[j].Kind == NzToken.RParen) depth--;
            j++;
        }
        int bodyEnd = j;

        // Find SELECT in body
        for (int k = bodyStart + 1; k < bodyEnd && k < tokens.Length; k++)
        {
            if (tokens[k].Kind == NzToken.Select)
            {
                // Extract column names from SELECT list
                var cols = new List<string>();
                int s = k + 1;
                if (s < bodyEnd && tokens[s].Kind is NzToken.Distinct or NzToken.All)
                    s++;
                int itemStart = s;
                for (int si = s; si < bodyEnd; si++)
                {
                    var sk = tokens[si].Kind;
                    if (sk is NzToken.From or NzToken.Into or NzToken.Where
                        or NzToken.GroupBy or NzToken.Having or NzToken.OrderBy
                        or NzToken.Limit or NzToken.Comma)
                    {
                        if (sk == NzToken.Comma)
                        {
                            // Extract name from range [itemStart, si)
                            var colName = ExtractAliasName(tokens, itemStart, si);
                            if (colName is not null) cols.Add(colName);
                            itemStart = si + 1;
                        }
                        else
                        {
                            // End of SELECT list
                            var colName = ExtractAliasName(tokens, itemStart, si);
                            if (colName is not null) cols.Add(colName);
                            break;
                        }
                    }
                }
                return cols;
            }
        }
        return new List<string>();
    }

    private static string? ExtractAliasName(Token<NzToken>[] tokens, int start, int end)
    {
        // Look for AS alias at the end: expr AS name
        for (int i = end - 1; i > start; i--)
        {
            if (tokens[i].Kind == NzToken.As && i + 1 < end
                && tokens[i + 1].Kind == NzToken.Identifier)
                return tokens[i + 1].ToStringValue();
        }
        // No AS — return last identifier if single
        if (end - start == 1 && tokens[start].Kind == NzToken.Identifier)
            return tokens[start].ToStringValue();
        if (end > start && tokens[end - 1].Kind == NzToken.Identifier)
        {
            bool allOk = true;
            for (int i = start; i < end; i++)
                if (tokens[i].Kind != NzToken.Identifier && tokens[i].Kind != NzToken.Dot)
                { allOk = false; break; }
            if (allOk)
                return tokens[end - 1].ToStringValue();
        }
        return null;
    }

    private static bool IsCteColumnListStart(Token<NzToken>[] tokens, int pos)
    {
        if (pos >= tokens.Length || tokens[pos].Kind != NzToken.LParen) return false;
        return pos + 1 < tokens.Length && tokens[pos + 1].Kind == NzToken.Identifier;
    }

    private static void SkipBalancedParens(Token<NzToken>[] tokens, ref int pos)
    {
        if (pos >= tokens.Length || tokens[pos].Kind != NzToken.LParen) return;
        int depth = 0;
        while (pos < tokens.Length)
        {
            if (tokens[pos].Kind == NzToken.LParen) depth++;
            else if (tokens[pos].Kind == NzToken.RParen) { depth--; if (depth == 0) { pos++; return; } }
            pos++;
        }
    }

    private static bool AllocBalancedParensSkip(Token<NzToken>[] tokens, ref int pos)
    {
        if (pos >= tokens.Length || tokens[pos].Kind != NzToken.LParen) return false;
        SkipBalancedParens(tokens, ref pos);
        return true;
    }

    private string? ResolveAliasTip(Token<NzToken>[] allTokens, int index, string word)
    {
        // Check if this identifier is at a position where FROM alias or JOIN alias is expected
        // Look for pattern: FROM table word or JOIN table word before this position
        bool foundFrom = false;
        string? tableName = null;

        for (int i = 0; i < allTokens.Length; i++)
        {
            var k = allTokens[i].Kind;

            if (k == NzToken.From || k == NzToken.Join)
            {
                foundFrom = true;
                tableName = null;
                continue;
            }
            if (k == NzToken.Where || k == NzToken.On || k == NzToken.Set
                || k == NzToken.GroupBy || k == NzToken.OrderBy || k == NzToken.Having
                || k == NzToken.As)
            {
                foundFrom = false;
                continue;
            }
            if (k == NzToken.Comma)
            {
                tableName = null;
                continue;
            }

            if (foundFrom && k == NzToken.Identifier)
            {
                if (tableName is null)
                {
                    tableName = allTokens[i].ToStringValue();
                }
                else if (string.Equals(allTokens[i].ToStringValue(), word, StringComparison.OrdinalIgnoreCase))
                {
                    // Found alias → resolve to table
                    var resolved = tableName;
                    // Check if the resolved name is a CTE
                    var cteTip = ResolveCteTip(allTokens, resolved);
                    if (cteTip is not null)
                    {
                        // Show alias → CTE info
                        return $"`{word}` → **{resolved}** (alias → CTE)";
                    }

                    // Show alias → table with columns
                    if (_parserSchema is not null)
                    {
                        var info = _parserSchema.GetTable(null, null, resolved);
                        if (info?.Columns is not null && info.Columns.Count > 0)
                        {
                            var lines = new List<string> { $"`{word}` → **{resolved}** (alias)" };
                            foreach (var col in info.Columns)
                                lines.Add($"- `{col.Name}`");
                            return string.Join("\n", lines);
                        }
                    }
                    return $"`{word}` → **{resolved}** (alias)";
                }
                else
                {
                    // This identifier is the table name itself (or previous alias)
                    // Don't update tableName — the alias is likely next
                    // Actually, tableName was set, so the next identifier is the alias
                    // But it matched word? No, we checked and it didn't match.
                    // So this is a different alias. Reset.
                    tableName = allTokens[i].ToStringValue();
                }
            }
        }
        return null;
    }

    private string? GetKeywordTooltip(NzToken kind, string word)
    {
        return kind switch
        {
            NzToken.Select => "SELECT — Retrieve rows from a table or view.",
            NzToken.From => "FROM — Specify the table(s) to query.",
            NzToken.Where => "WHERE — Filter rows based on a condition.",
            NzToken.Insert => "INSERT — Add new rows to a table.",
            NzToken.Into => "INTO — Specify the target table for INSERT.",
            NzToken.Values or NzToken.Value => "VALUES — Specify row values.",
            NzToken.Update => "UPDATE — Modify existing rows in a table.",
            NzToken.Set => "SET — Specify column assignments in UPDATE.",
            NzToken.Delete => "DELETE — Remove rows from a table.",
            NzToken.Join => "JOIN — Combine rows from two tables.",
            NzToken.Inner => "INNER — Return matching rows.",
            NzToken.Left => "LEFT — Return all rows from left table.",
            NzToken.Right => "RIGHT — Return all rows from right table.",
            NzToken.Full => "FULL — Return all rows from both tables.",
            NzToken.Cross => "CROSS — Return Cartesian product.",
            NzToken.On => "ON — Specify join condition.",
            NzToken.And => "AND — Both conditions must be true.",
            NzToken.Or => "OR — At least one condition must be true.",
            NzToken.Not => "NOT — Negate a condition.",
            NzToken.As => "AS — Assign an alias.",
            NzToken.Distinct => "DISTINCT — Remove duplicate rows.",
            NzToken.All => "ALL — Include all rows.",
            NzToken.Union => "UNION — Combine queries (distinct).",
            NzToken.Intersect => "INTERSECT — Return common rows.",
            NzToken.Except => "EXCEPT — Rows from first not in second.",
            NzToken.Having => "HAVING — Filter groups.",
            NzToken.Limit => "LIMIT — Restrict result rows.",
            NzToken.Offset => "OFFSET — Skip rows.",
            NzToken.Null => "NULL — Missing data.",
            NzToken.Is => "IS — Test boolean condition.",
            NzToken.Like => "LIKE — Pattern match.",
            NzToken.Ilike => "ILIKE — Case-insensitive pattern match.",
            NzToken.In => "IN — Test if value is in list.",
            NzToken.Between => "BETWEEN — Test if value is in range.",
            NzToken.Exists => "EXISTS — Test if subquery returns rows.",
            NzToken.Case => "CASE — Conditional expression.",
            NzToken.When => "WHEN — Specify CASE condition.",
            NzToken.Then => "THEN — Specify CASE result.",
            NzToken.Else => "ELSE — Default CASE result.",
            NzToken.End => "END — End a block or CASE.",
            NzToken.Begin => "BEGIN — Start a block.",
            NzToken.Declare => "DECLARE — Declare a variable.",
            NzToken.Create => "CREATE — Create a database object.",
            NzToken.Table => "TABLE — Create or reference a table.",
            NzToken.View or NzToken.Views => "VIEW — Create or reference a view.",
            NzToken.Drop => "DROP — Remove a database object.",
            NzToken.Alter => "ALTER — Modify a database object.",
            NzToken.Truncate => "TRUNCATE — Remove all rows (cannot be rolled back).",
            NzToken.With => "WITH — Define a Common Table Expression (CTE).",
            NzToken.Recursive => "RECURSIVE — Allow CTE to reference itself.",
            NzToken.Explain => "EXPLAIN — Show query execution plan.",
            NzToken.Cast => "CAST — Convert a value to a different data type.",
            NzToken.Over => "OVER — Define a window for analytical functions.",
            NzToken.PartitionBy => "PARTITION BY — Divide rows into partitions.",
            NzToken.Fetch => "FETCH — Retrieve a subset of rows.",
            NzToken.For => "FOR — Used in loops, UPDATE...FOR, or FOR XML.",
            NzToken.Call => "CALL — Execute a stored procedure.",
            NzToken.Return => "RETURN — Return from a function or procedure.",
            NzToken.Merge => "MERGE — Insert, update, or delete rows based on source data.",
            NzToken.GroupBy => "GROUP BY — Group rows for aggregation.",
            NzToken.OrderBy => "ORDER BY — Sort the result set.",
            NzToken.Nulls => "NULLS FIRST / NULLS LAST — Control NULL ordering.",
            NzToken.Asc => "ASC — Sort ascending (default).",
            NzToken.Desc => "DESC — Sort descending.",
            NzToken.Groom => "GROOM — Reclaim space or manage table versions.",
            NzToken.Versions => "VERSIONS — Groom table version chains.",
            NzToken.Records => "RECORDS — Groom deleted records.",
            NzToken.Pages => "PAGES — Groom table pages.",
            NzToken.Reclaim => "RECLAIM — Reclaim space after grooming.",
            NzToken.Backupset => "BACKUPSET — Include backupset in reclaim.",
            NzToken.Generate => "GENERATE — Create or update database statistics.",
            NzToken.Statistics => "STATISTICS — Database statistics for query optimization.",
            NzToken.Express => "EXPRESS — Quick statistics sampling.",
            NzToken.Distribute => "DISTRIBUTE ON — Define table distribution key.",
            NzToken.Random => "RANDOM — Random distribution (no hash key).",
            NzToken.Organize => "ORGANIZE ON — Define table organization key.",
            NzToken.External => "EXTERNAL — External table definition.",
            NzToken.SameAs => "SAMEAS — Copy column definitions from another table.",
            NzToken.Hash => "HASH — Hash-based distribution.",
            NzToken.Grant => "GRANT — Grant privileges.",
            NzToken.Revoke => "REVOKE — Revoke privileges.",
            NzToken.Public => "PUBLIC — Public role.",
            NzToken.Cascade => "CASCADE — Cascade the operation.",
            NzToken.Restrict => "RESTRICT — Restrict the operation.",
            NzToken.Commit => "COMMIT — Commit the current transaction.",
            NzToken.Rollback => "ROLLBACK — Roll back the current transaction.",
            NzToken.Nzplsql => "NZPLSQL — Netezza stored procedure language.",
            NzToken.BeginProc => "BEGIN_PROC — Start of NZPLSQL procedure.",
            NzToken.EndProc => "END_PROC — End of NZPLSQL procedure.",
            NzToken.Exception => "EXCEPTION — Exception handler block.",
            NzToken.Constant => "CONSTANT — Constant variable declaration.",
            NzToken.Loop => "LOOP — Infinite loop construct.",
            NzToken.While => "WHILE — Conditional loop construct.",
            NzToken.Exit => "EXIT — Exit a loop (optional WHEN condition).",
            NzToken.Raise => "RAISE — Raise a notice, warning, exception, or error.",
            NzToken.Notice => "NOTICE — Informational message level.",
            NzToken.Debug => "DEBUG — Debug message level.",
            NzToken.Error1 => "ERROR — Error message level.",
            NzToken.Immediate => "IMMEDIATE — Used with EXECUTE IMMEDIATE for dynamic SQL.",
            NzToken.Using => "USING — Specify USING clause for JOIN or EXECUTE IMMEDIATE.",
            NzToken.Verbose => "VERBOSE — Show detailed information.",
            NzToken.Distribution => "DISTRIBUTION — Show data distribution in EXPLAIN.",
            NzToken.Plantext => "PLANTEXT — Show plan text in EXPLAIN.",
            NzToken.Plangraph => "PLANGRAPH — Show plan graph in EXPLAIN.",
            NzToken.Next => "NEXT — Sequence value (NEXT VALUE FOR).",
            NzToken.Default => "DEFAULT — Default column value.",
            NzToken.Unique => "UNIQUE — Unique constraint.",
            NzToken.Primary => "PRIMARY — Primary key constraint.",
            NzToken.Key => "KEY — Key in constraint definition.",
            NzToken.Foreign => "FOREIGN KEY — Foreign key constraint.",
            NzToken.References => "REFERENCES — Referenced table in foreign key.",
            NzToken.Check => "CHECK — Check constraint.",
            NzToken.Constraint => "CONSTRAINT — Named constraint.",
            NzToken.Add => "ADD — Add column or constraint.",
            NzToken.Execute or NzToken.Exec => "EXECUTE — Execute a procedure or dynamic SQL.",
            NzToken.Owner => "OWNER — Procedure execution owner context.",
            NzToken.Caller => "CALLER — Procedure execution caller context.",
            NzToken.Comment => "COMMENT ON — Add a comment to a database object.",
            _ => null,
        };
    }

    private static bool IsDataType(string word)
    {
        return word.Length > 1 && DataTypeNames.Contains(word.ToUpperInvariant());
    }

    private static readonly HashSet<string> DataTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INT1", "INT2", "INT4", "INT8", "INTEGER",
        "BIGINT", "SMALLINT", "BYTEINT",
        "NUMERIC", "DECIMAL", "FLOAT", "FLOAT4", "FLOAT8", "REAL", "DOUBLE", "DOUBLE PRECISION",
        "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "CHARACTER", "CHARACTER VARYING", "CHAR VARYING",
        "BOOLEAN", "BOOL",
        "DATE", "TIME", "TIMETZ", "TIMESTAMP", "TIMESTAMPTZ", "INTERVAL",
        "BINARY", "VARBINARY", "BLOB", "CLOB", "NCLOB",
        "TEXT", "NTEXT",
    };

    private static string GetDataTypeTip(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "INT" or "INT4" or "INTEGER" => $"**{name}** — 32-bit signed integer",
            "INT1" or "BYTEINT" => $"**{name}** — 8-bit signed integer",
            "INT2" or "SMALLINT" => $"**{name}** — 16-bit signed integer",
            "INT8" or "BIGINT" => $"**{name}** — 64-bit signed integer",
            "NUMERIC" or "DECIMAL" => $"**{name}** — Exact numeric with precision/scale",
            "VARCHAR" => $"**VARCHAR(n)** — Variable-length character string",
            "NVARCHAR" => $"**NVARCHAR(n)** — Variable-length Unicode string",
            "CHAR" or "NCHAR" or "CHARACTER" => $"**{name}(n)** — Fixed-length character string",
            "CHARACTER VARYING" or "CHAR VARYING" => $"**{name}(n)** — Variable-length character string",
            "BOOLEAN" or "BOOL" => $"**{name}** — Boolean (TRUE/FALSE/NULL)",
            "DATE" => $"**DATE** — Calendar date",
            "TIME" => $"**TIME** — Time of day",
            "TIMETZ" => $"**TIMETZ** — Time with time zone",
            "TIMESTAMP" => $"**TIMESTAMP** — Date and time",
            "TIMESTAMPTZ" => $"**TIMESTAMPTZ** — Date and time with time zone",
            "INTERVAL" => $"**INTERVAL** — Time interval",
            "BLOB" => $"**BLOB** — Binary large object",
            "CLOB" => $"**CLOB** — Character large object",
            "TEXT" => $"**TEXT** — Variable-length character string",
            _ => $"**{name}** — Data type",
        };
    }

    private string? ResolveColumnTip(string tableName, string columnName)
    {
        if (_parserSchema is null)
            return $"`{tableName}.{columnName}`";

        var info = _parserSchema.GetTable(null, null, tableName);
        if (info?.Columns is not null)
        {
            var col = info.Columns.FirstOrDefault(c =>
                c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (col is not null)
                return $"`{tableName}.{columnName}`\nTable: **{tableName}**";
        }
        return $"`{tableName}.{columnName}`";
    }

    private static string GetFunctionTip(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "COUNT" => "**COUNT(** *expr* **)** — Number of rows. `COUNT(*)` for all rows.",
            "SUM" => "**SUM(** *expr* **)** — Sum of values.",
            "AVG" => "**AVG(** *expr* **)** — Average of values.",
            "MIN" => "**MIN(** *expr* **)** — Minimum value.",
            "MAX" => "**MAX(** *expr* **)** — Maximum value.",
            "COALESCE" => "**COALESCE(** *val1*, *val2*, ... **)** — First non-NULL argument.",
            "NULLIF" => "**NULLIF(** *a*, *b* **)** — NULL if a = b, else a.",
            "NVL" or "NVL2" => $"**{name}(** *expr*, *default* **)** — Default if NULL.",
            "UPPER" => "**UPPER(** *str* **)** — Convert to uppercase.",
            "LOWER" => "**LOWER(** *str* **)** — Convert to lowercase.",
            "TRIM" => "**TRIM(** *str* **)** — Remove leading and trailing spaces.",
            "BTRIM" => "**BTRIM(** *str* **)** — Remove leading and trailing spaces (alias for TRIM).",
            "LENGTH" => "**LENGTH(** *str* **)** — String length.",
            "SUBSTR" or "SUBSTRING" => $"**{name}(** *str*, *start* [, *length*] **)** — Extract substring.",
            "STRPOS" => "**STRPOS(** *str*, *substr* **)** — Position of substring.",
            "REPLACE" => "**REPLACE(** *str*, *from*, *to* **)** — Replace substring occurrences.",
            "TRANSLATE" => "**TRANSLATE(** *str*, *from*, *to* **)** — Character-level replacement.",
            "CONCAT" => "**CONCAT(** *str1*, *str2*, ... **)** — Concatenate strings.",
            "TO_CHAR" => "**TO_CHAR(** *value*, *format* **)** — Format value as string.",
            "TO_DATE" => "**TO_DATE(** *str*, *format* **)** — Convert string to date.",
            "TO_NUMBER" => "**TO_NUMBER(** *str* [, *format*] **)** — Convert string to number.",
            "TO_TIMESTAMP" => "**TO_TIMESTAMP(** *str*, *format* **)** — Convert string to timestamp.",
            "EXTRACT" => "**EXTRACT(** *part* FROM *source* **)** — Extract date/time part (YEAR, MONTH, DAY, HOUR, etc.).",
            "DATE_PART" => "**DATE_PART(** *part*, *date* **)** — Extract date part as numeric.",
            "DATE_TRUNC" => "**DATE_TRUNC(** *precision*, *date* **)** — Truncate date to precision.",
            "ADD_MONTHS" => "**ADD_MONTHS(** *date*, *n* **)** — Add n months to date.",
            "MONTHS_BETWEEN" => "**MONTHS_BETWEEN(** *date1*, *date2* **)** — Months between two dates.",
            "DAYS_BETWEEN" => "**DAYS_BETWEEN(** *date1*, *date2* **)** — Days between two dates.",
            "MONTH" => "**MONTH(** *date* **)** — Month number (1-12).",
            "YEAR" => "**YEAR(** *date* **)** — Year number.",
            "DAY" => "**DAY(** *date* **)** — Day of month (1-31).",
            "HOUR" => "**HOUR(** *time* **)** — Hour (0-23).",
            "FIRST_DAY" => "**FIRST_DAY(** *date* **)** — First day of month.",
            "LAST_DAY" => "**LAST_DAY(** *date* **)** — Last day of month.",
            "NEXT_MONTH" => "**NEXT_MONTH(** *date* **)** — Same day next month.",
            "NEXT_YEAR" => "**NEXT_YEAR(** *date* **)** — Same day next year.",
            "NEXT_QUARTER" => "**NEXT_QUARTER(** *date* **)** — Same day next quarter.",
            "NEXT_WEEK" => "**NEXT_WEEK(** *date* **)** — Same day next week.",
            "THIS_MONTH" => "**THIS_MONTH()** — Current month start.",
            "THIS_YEAR" => "**THIS_YEAR()** — Current year start.",
            "THIS_QUARTER" => "**THIS_QUARTER()** — Current quarter start.",
            "THIS_WEEK" => "**THIS_WEEK()** — Current week start.",
            "AGE" => "**AGE(** *date1*, *date2* **)** — Interval between dates.",
            "DURATION_ADD" => "**DURATION_ADD(** *interval*, *duration* **)** — Add duration to interval.",
            "DURATION_SUBTRACT" => "**DURATION_SUBTRACT(** *interval*, *duration* **)** — Subtract duration.",
            "ABS" => "**ABS(** *num* **)** — Absolute value.",
            "CEIL" or "CEILING" => $"**{name}(** *num* **)** — Round up to nearest integer.",
            "FLOOR" => "**FLOOR(** *num* **)** — Round down to nearest integer.",
            "ROUND" => "**ROUND(** *num* [, *d*] **)** — Round to d decimal places.",
            "TRUNC" => "**TRUNC(** *num* [, *d*] **)** — Truncate to d decimal places.",
            "MOD" => "**MOD(** *a*, *b* **)** — Remainder of a / b.",
            "POW" or "POWER" => $"**{name}(** *base*, *exp* **)** — Exponentiation.",
            "FPOW" => "**FPOW(** *base*, *exp* **)** — Floating-point exponentiation.",
            "SQRT" => "**SQRT(** *num* **)** — Square root.",
            "NUMERIC_SQRT" => "**NUMERIC_SQRT(** *num* **)** — Square root (numeric result).",
            "RANDOM" => "**RANDOM()** — Random value between 0.0 and 1.0.",
            "SETSEED" => "**SETSEED(** *seed* **)** — Set random seed for reproducible RANDOM().",
            "WIDTH_BUCKET" => "**WIDTH_BUCKET(** *expr*, *min*, *max*, *buckets* **)** — Histogram bucket.",
            "GREATEST" or "GREATER" => $"**{name}(** *val1*, *val2*, ... **)** — Largest value.",
            "LEAST" => "**LEAST(** *val1*, *val2*, ... **)** — Smallest value.",
            "DECODE" => "**DECODE(** *expr*, *search1*, *result1* [, ...] [, *default*] **)** — Case-like search.",
            "ROW_NUMBER" => "**ROW_NUMBER() OVER(** *...* **)** — Sequential row number within window.",
            "RANK" => "**RANK() OVER(** *...* **)** — Rank with gaps for ties.",
            "DENSE_RANK" => "**DENSE_RANK() OVER(** *...* **)** — Rank without gaps.",
            "NTILE" => "**NTILE(** *n* **) OVER(** *...* **)** — Divide rows into n buckets.",
            "LEAD" => "**LEAD(** *expr*, *offset*, *default* **) OVER(** *...* **)** — Value from next row.",
            "LAG" => "**LAG(** *expr*, *offset*, *default* **) OVER(** *...* **)** — Value from previous row.",
            "FIRST_VALUE" => "**FIRST_VALUE(** *expr* **) OVER(** *...* **)** — First value in window.",
            "LAST_VALUE" => "**LAST_VALUE(** *expr* **) OVER(** *...* **)** — Last value in window.",
            "NTH_VALUE" => "**NTH_VALUE(** *expr*, *n* **) OVER(** *...* **)** — Nth value in window.",
            "STRING_AGG" => "**STRING_AGG(** *expr*, *delimiter* **) [ORDER BY ...]** — Aggregate values to string.",
            "LISTAGG" => "**LISTAGG(** *expr*, *delimiter* **) [WITHIN GROUP (ORDER BY ...)]** — Aggregate to string.",
            "STDDEV" => "**STDDEV(** *expr* **)** — Standard deviation.",
            "STDDEV_POP" => "**STDDEV_POP(** *expr* **)** — Population standard deviation.",
            "STDDEV_SAMP" => "**STDDEV_SAMP(** *expr* **)** — Sample standard deviation.",
            "VARIANCE" => "**VARIANCE(** *expr* **)** — Variance.",
            "VAR_POP" => "**VAR_POP(** *expr* **)** — Population variance.",
            "VAR_SAMP" => "**VAR_SAMP(** *expr* **)** — Sample variance.",
            "MEDIAN" => "**MEDIAN(** *expr* **)** — Median value.",
            "HASH" => "**HASH(** *expr* **)** — Hash value.",
            "HASH4" => "**HASH4(** *expr* **)** — 32-bit hash value.",
            "HASH8" => "**HASH8(** *expr* **)** — 64-bit hash value.",
            "INSTR" => "**INSTR(** *str*, *substr* [, *pos*, *occurrence*] **)** — Substring position.",
            "CONVERT" => "**CONVERT(** *type*, *expr* **)** — Convert between data types.",
            "FORMAT" => "**FORMAT(** *value*, *format* **)** — Format value as string.",
            "GET_VIEWDEF" => "**GET_VIEWDEF(** *name* **)** — Get view definition SQL.",
            "VERSION" => "**VERSION()** — Netezza version string.",
            "TIMEOFDAY" => "**TIMEOFDAY()** — Current timestamp as text with time zone.",
            "TIMEZONE" => "**TIMEZONE(** *zone*, *timestamp* **)** — Convert time zone.",
            "ISFALSE" or "ISNOTFALSE" or "ISTRUE" or "ISNOTTRUE" => $"**{name}()** — Boolean test function.",
            "BITAND" or "BITOR" or "BITXOR" or "BITNOT" => $"**{name}(** *a*, *b* **)** — Bitwise operation.",
            "INT1AND" or "INT2AND" or "INT4AND" or "INT8AND" => $"**{name}(** *a*, *b* **)** — Bitwise AND (INT type).",
            "INT1OR" or "INT2OR" or "INT4OR" or "INT8OR" => $"**{name}(** *a*, *b* **)** — Bitwise OR (INT type).",
            "INT1XOR" or "INT2XOR" or "INT4XOR" or "INT8XOR" => $"**{name}(** *a*, *b* **)** — Bitwise XOR (INT type).",
            "INT1NOT" or "INT2NOT" or "INT4NOT" or "INT8NOT" => $"**{name}(** *a* **)** — Bitwise NOT (INT type).",
            "INT1INCR" or "INT2INCR" or "INT4INCR" or "INT8INCR" => $"**{name}(** *a* **)** — Increment (INT type).",
            "INT1DECR" or "INT2DECR" or "INT4DECR" or "INT8DECR" => $"**{name}(** *a* **)** — Decrement (INT type).",
            "INT1SHL" or "INT2SHL" or "INT4SHL" or "INT8SHL" => $"**{name}(** *a*, *bits* **)** — Shift left (INT type).",
            "INT1SHR" or "INT2SHR" or "INT4SHR" or "INT8SHR" => $"**{name}(** *a*, *bits* **)** — Shift right (INT type).",
            "INT_TO_STRING" => "**INT_TO_STRING(** *num* **)** — Convert integer to string.",
            "STRING_TO_INT" => "**STRING_TO_INT(** *str* **)** — Convert string to integer.",
            "HEX_TO_BINARY" => "**HEX_TO_BINARY(** *hex* **)** — Convert hex string to binary.",
            "HEX_TO_GEOMETRY" => "**HEX_TO_GEOMETRY(** *hex* **)** — Convert hex to geometry.",
            "OVERLAPS" => "**OVERLAPS(** *range1*, *range2* **)** — Test if time ranges overlap.",
            "REGEXP_LIKE" => "**REGEXP_LIKE(** *str*, *pattern* **)** — Test regex match.",
            "REGEXP_REPLACE" => "**REGEXP_REPLACE(** *str*, *pattern*, *replacement* **)** — Regex replace.",
            "REGEXP_SUBSTR" => "**REGEXP_SUBSTR(** *str*, *pattern* **)** — Extract regex match.",
            "UNICHR" => "**UNICHR(** *code* **)** — Unicode character from code point.",
            "UNICODE" or "UNICODES" => $"**{name}(** *str* **)** — Unicode code point of character.",
            "DCEIL" => "**DCEIL(** *num* **)** — Decimal ceiling.",
            "DFLOOR" => "**DFLOOR(** *num* **)** — Decimal floor.",
            "CURRENT_DATE" => "**CURRENT_DATE** — Current date (session time zone).",
            "CURRENT_TIMESTAMP" => "**CURRENT_TIMESTAMP** — Current date and time.",
            "CURRENT_TIME" => "**CURRENT_TIME** — Current time.",
            "NOW" => "**NOW()** — Current date and time.",
            _ => $"**{name}()** — Built-in function",
        };
    }

    private static bool IsKnownFunction(string name)
    {
        return name.Length > 1 && KnownFunctionNames.Contains(name.ToUpperInvariant());
    }

    private static readonly HashSet<string> KnownFunctionNames = new()
    {
        "ABS", "ADD_MONTHS", "AGE", "AVG", "BITAND", "BITNOT", "BITOR", "BITXOR",
        "BTRIM", "CEIL", "CEILING", "COALESCE", "CONCAT", "CONVERT", "COUNT",
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "DATE_PART", "DATE_TRUNC", "DAY", "DAYS_BETWEEN", "DCEIL", "DECODE", "DENSE_RANK", "DFLOOR",
        "DURATION_ADD", "DURATION_SUBTRACT",
        "EXTRACT", "FIRST_DAY", "FIRST_VALUE", "FLOOR", "FORMAT", "FPOW",
        "GET_VIEWDEF", "GREATER", "GREATEST",
        "HASH", "HASH4", "HASH8", "HEX_TO_BINARY", "HEX_TO_GEOMETRY", "HOUR", "HOURS_BETWEEN",
        "INSTR", "INT_TO_STRING",
        "INT1AND", "INT1OR", "INT1XOR", "INT1NOT", "INT1INCR", "INT1DECR", "INT1SHL", "INT1SHR",
        "INT2AND", "INT2OR", "INT2XOR", "INT2NOT", "INT2INCR", "INT2DECR", "INT2SHL", "INT2SHR",
        "INT4AND", "INT4OR", "INT4XOR", "INT4NOT", "INT4INCR", "INT4DECR", "INT4SHL", "INT4SHR",
        "INT8AND", "INT8OR", "INT8XOR", "INT8NOT", "INT8INCR", "INT8DECR", "INT8SHL", "INT8SHR",
        "ISFALSE", "ISNOTFALSE", "ISNOTTRUE", "ISTRUE",
        "LAG", "LAST_DAY", "LAST_VALUE", "LEAD", "LEAST", "LENGTH",
        "LISTAGG", "LOWER", "MAX", "MEDIAN", "MIN", "MINUTES_BETWEEN", "MOD", "MONTH",
        "MONTHS_BETWEEN", "NEXT_MONTH", "NEXT_QUARTER", "NEXT_WEEK", "NEXT_YEAR",
        "NTH_VALUE", "NTILE", "NULLIF", "NUMERIC_SQRT", "NVL", "NVL2", "NOW",
        "OVERLAPS", "POW", "POWER", "RANDOM", "RANK",
        "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR",
        "REPLACE", "ROUND", "ROW_NUMBER",
        "SECONDS_BETWEEN", "SETSEED", "SQRT", "STDDEV", "STDDEV_POP", "STDDEV_SAMP", "STRING_AGG",
        "STRING_TO_INT", "STRPOS", "SUBSTR", "SUBSTRING", "SUM",
        "THIS_MONTH", "THIS_QUARTER", "THIS_WEEK", "THIS_YEAR",
        "TIMEOFDAY", "TIMEZONE", "TO_CHAR", "TO_DATE", "TO_NUMBER", "TO_TIMESTAMP",
        "TRANSLATE", "TRIM", "TRUNC", "UNICHR", "UNICODE", "UNICODES",
        "UPPER", "VARIANCE", "VAR_POP", "VAR_SAMP", "VERSION", "WEEKS_BETWEEN", "WIDTH_BUCKET", "YEAR", "YEARS_BETWEEN",
    };

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
