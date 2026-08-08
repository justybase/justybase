using JustyBase.Helpers;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Netezza.Completion;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Editor.CompletionProviders;

public partial class SqlCompletionProvider : ICodeEditorCompletionProvider
{
    private readonly ISqlAutocompleteData _sqlAutocompleteData;
    private readonly SnippetInfoService _snippetService;
    private readonly SqlCodeEditor _sqlCodeEditor;
    private readonly ISomeEditorOptions? _someEditorOptions;
    private readonly NzCompletionEngine? _completionEngine;
    private readonly DocumentParsingCoordinator? _parsingCoordinator;
    private readonly string? _documentUri;
    private readonly Action<string, string, string>? _ensureTableColumns;
    private readonly InMemorySchemaProvider? _parserSchema;
    private SqlDialect _dialect = SqlDialect.Netezza;

    private static readonly object EngineCacheLock = new();
    private static readonly Dictionary<SqlDialect, NzCompletionEngine> EngineCache = new();

    private const string FastSnippetTxt = "fast";

    public SqlCompletionProvider(SqlCodeEditor sqlCodeEditor, ISqlAutocompleteData sqlAutocompleteData,
        ISomeEditorOptions snippetsProvider, NzCompletionEngine? completionEngine = null,
        DocumentParsingCoordinator? parsingCoordinator = null, string? documentUri = null,
        Action<string, string, string>? ensureTableColumns = null,
        InMemorySchemaProvider? parserSchema = null,
        SqlDialect dialect = SqlDialect.Netezza)
    {
        _someEditorOptions = snippetsProvider;
        _snippetService = new SnippetInfoService(_someEditorOptions);
        _sqlAutocompleteData = sqlAutocompleteData;
        _sqlCodeEditor = sqlCodeEditor;
        _completionEngine = completionEngine;
        _parsingCoordinator = parsingCoordinator;
        _documentUri = documentUri;
        _ensureTableColumns = ensureTableColumns;
        _parserSchema = parserSchema;
        _dialect = dialect;
    }

    /// <summary>
    /// Switches the completion/signature-help surface to another SQL dialect
    /// (e.g. Db2 uses the Db2Lexer and Db2SqlCatalog from JustyBase.NetezzaSql).
    /// </summary>
    public void SetDialect(SqlDialect dialect)
    {
        _dialect = dialect;
    }

    private NzCompletionEngine GetCompletionEngine()
    {
        if (_dialect == SqlDialect.Netezza)
            return _completionEngine ?? CreateEngine(SqlDialect.Netezza);

        return CreateEngine(_dialect);
    }

    private NzCompletionEngine CreateEngine(SqlDialect dialect)
    {
        lock (EngineCacheLock)
        {
            if (EngineCache.TryGetValue(dialect, out var cached))
                return cached;

            var engine = new NzCompletionEngine(
                _parserSchema,
                _parsingCoordinator,
                catalog: DialectRuntime.AuthoringCatalog(dialect),
                dialect: dialect);
            EngineCache[dialect] = engine;
            return engine;
        }
    }

    public async Task<CompletionResult> GetCompletionData(int position, char? triggerChar, CompletionRequestKind requestKind = CompletionRequestKind.Completion)
    {
        if (requestKind == CompletionRequestKind.SignatureHelp)
        {
            var rawSql = _sqlCodeEditor.Document?.Text ?? string.Empty;
            var signatureHelp = NzSignatureHelpService.GetSignatureHelp(rawSql, position, _parsingCoordinator, _documentUri,
                catalog: DialectRuntime.AuthoringCatalogOrNull(_dialect), dialect: _dialect);
            return new CompletionResult(
                Array.Empty<ICompletionDataEx>(),
                signatureHelp is null ? null : new SqlOverloadProvider(signatureHelp),
                true);
        }

        // Whitespace (space/tab/newline) never opens the completion list by itself;
        // only a word character, '.' or an explicit Ctrl+Space (triggerChar == null) does.
        if (IsSuppressedTrigger(triggerChar))
            return new CompletionResult([], null, true);

        string? lastWord = EditorHelpers.GetLastWord(_sqlCodeEditor, position);
        var rawSqlText = _sqlCodeEditor.Document?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(lastWord))
        {
            // Explicit Ctrl+Space (triggerChar == null) always shows the full context list.
            bool isExplicit = triggerChar is null;
            if (!isExplicit && (position <= 0 || position > rawSqlText.Length || rawSqlText[position - 1] != '.'))
                return new CompletionResult(Array.Empty<ICompletionDataEx>(), null, true);
        }

        var importExportCompletion = ImportExportCompletionHelper.GetCompletionForDirective(lastWord ?? string.Empty);
        if (importExportCompletion is not null)
            return importExportCompletion;

        var completionData = new List<ICompletionDataEx>();
        var engineItems = Array.Empty<CompletionItem>();
        Dictionary<string, List<string>> withHints = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> tempTableHints = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> aliasDbTable = new(StringComparer.OrdinalIgnoreCase);
        string legacySql = rawSqlText;

        var completionEngine = GetCompletionEngine();
        if (completionEngine is not null)
        {
            completionEngine.SetDocumentUri(_documentUri);
            int lineCount = _sqlCodeEditor.Document?.LineCount ?? SqlPerformancePolicy.CountLines(rawSqlText);
            // Dot / explicit invoke use the forced (48k) statement limit; passive timer uses 8k.
            bool forced = triggerChar is '.' or null;
            if (SqlAutocompleteWindow.ShouldRunEngine(rawSqlText, position, lineCount, forced))
            {
                var (engineSql, engineCursor) = SqlAutocompleteWindow.SliceForEngine(
                    rawSqlText, position, lineCount, forced);
                legacySql = engineSql;
                engineItems = completionEngine.GetCompletions(engineSql, engineCursor).ToArray();
                if (TryHydrateColumnsForDotCompletion(engineSql, engineCursor, engineItems))
                    engineItems = completionEngine.GetCompletions(engineSql, engineCursor).ToArray();
                if (ShouldRunLegacyPath(engineItems, engineSql))
                {
                    (withHints, tempTableHints, aliasDbTable) = completionEngine.GetScopeHints();
                }
                foreach (var ci in engineItems)
                {
                    completionData.Add(CompletionDataSql.FromEngineItem(ci, MapGlyph(ci.Kind)));
                }
            }
        }

        AddVariableCompletions(completionData, lastWord);
        AddSnippetCompletions(completionData, lastWord);

        if (ShouldRunLegacyPath(engineItems, legacySql))
            await AddLegacyDatabaseCompletions(completionData, lastWord ?? string.Empty, withHints, tempTableHints, aliasDbTable);

        return new CompletionResult(completionData, null, true);
    }

    public static bool ShouldRunLegacyPath(IReadOnlyList<CompletionItem> engineItems, string sql)
        => SqlCompletionMergePolicy.ShouldRunLegacyPath(engineItems, sql);

    /// <summary>
    /// True when the just-typed character must never open the completion list.
    /// Whitespace stays silent (VS Code-like); '.' and word characters trigger,
    /// and null (explicit Ctrl+Space) always shows the full list.
    /// </summary>
    public static bool IsSuppressedTrigger(char? triggerChar)
        => triggerChar is not null && char.IsWhiteSpace(triggerChar.Value);

    private void AddVariableCompletions(List<ICompletionDataEx> completionData, string lastWord)
    {
        var variablesDictionary = _someEditorOptions?.VariablesDictionary;
        if (variablesDictionary is null)
            return;

        foreach (var item in variablesDictionary)
        {
            if (item.Key.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase))
            {
                completionData.Add(new CompletionDataSql(item.Key[1..], $"value: {item.Value}", false, Glyph.None, null));
            }
        }
    }

    private void AddSnippetCompletions(List<ICompletionDataEx> completionData, string lastWord)
    {
        var snippets = _someEditorOptions?.GetAllSnippets;
        if (snippets is null)
            return;

        foreach (var (snippetName, snippetValue) in snippets)
        {
            if (snippetValue.snippetType != FastSnippetTxt &&
                snippetName.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase))
            {
                completionData.Add(new CompletionDataSql(
                    snippetName,
                    snippetValue.Description,
                    false,
                    Glyph.Snippet,
                    _snippetService.SnippetManager));
            }
        }
    }

    private async Task AddLegacyDatabaseCompletions(List<ICompletionDataEx> completionData, string lastWord,
        Dictionary<string, List<string>> withHints,
        Dictionary<string, List<string>> tempTableHints,
        Dictionary<string, List<string>> aliasDbTable)
    {
        if (int.TryParse(lastWord, out _))
            return;

        await foreach (var objectName in _sqlAutocompleteData.GetWordsList(
                           lastWord,
                           aliasDbTable,
                           new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                           withHints,
                           tempTableHints))
        {
            completionData.Add(objectName);
        }
    }

    private static Glyph MapGlyph(CompletionKind kind) => kind switch
    {
        CompletionKind.Keyword => Glyph.None,
        CompletionKind.Table => Glyph.Table,
        CompletionKind.View => Glyph.View,
        CompletionKind.Column => Glyph.Column,
        CompletionKind.Function => Glyph.Function,
        CompletionKind.Schema => Glyph.Schema,
        CompletionKind.Database => Glyph.Database,
        CompletionKind.Alias => Glyph.Table,
        CompletionKind.Cte => Glyph.WithDb,
        CompletionKind.DataType => Glyph.None,
        CompletionKind.Snippet => Glyph.Snippet,
        CompletionKind.Variable => Glyph.None,
        CompletionKind.ExternalTable => Glyph.ExternalTable,
        _ => Glyph.None
    };

    private bool TryHydrateColumnsForDotCompletion(
        string sql,
        int position,
        IReadOnlyList<CompletionItem> engineItems)
    {
        if (_ensureTableColumns is null) return false;
        if (position <= 0 || position > sql.Length || sql[position - 1] != '.') return false;
        if (engineItems.Any(i => i.Kind == CompletionKind.Column)) return false;

        var before = sql[..(position - 1)];
        var parts = before.Split([' ', '\n', '\t', '\r', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var qualifier = parts[^1];

        string database = "", schema = "", table;
        try
        {
            var tokens = DialectRuntime.Tokenize(sql, _dialect).ToArray();
            var resolved = CompletionAliasResolver.ResolveTablePath(tokens, qualifier);
            if (resolved is null)
            {
                // Bare unknown identifiers (e.g. NO_SUCH_ALIAS.) must not hydrate
                // as if they were a table. Qualified paths (schema.table / db..table)
                // are still allowed so catalog completion keeps working.
                var isQualifiedPath = qualifier.Contains('.', StringComparison.Ordinal);
                if (!isQualifiedPath)
                    return false;
                resolved = CompletionAliasResolver.ParseQualifierPath(qualifier);
            }

            database = resolved.Value.Database ?? "";
            schema = resolved.Value.Schema ?? "";
            table = resolved.Value.Name;
        }
        catch
        {
            var segs = qualifier.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length is < 1 or > 3) return false;
            // Same guard as above: do not treat a single unknown token as a table.
            if (segs.Length == 1) return false;
            if (segs.Length == 3) { database = segs[0]; schema = segs[1]; table = segs[2]; }
            else { schema = segs[0]; table = segs[1]; }
        }

        if (string.IsNullOrWhiteSpace(table)) return false;
        _ensureTableColumns(database, schema, table);
        return true;
    }
}
