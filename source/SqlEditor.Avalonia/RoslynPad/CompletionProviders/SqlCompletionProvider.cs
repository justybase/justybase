using JustyBase.Helpers;
using JustyBase.Core.Database;
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
    private readonly SnippetInfoService _snippetService;
    private readonly SqlCodeEditor _sqlCodeEditor;
    private readonly ISomeEditorOptions? _someEditorOptions;
    private readonly DocumentParsingCoordinator? _parsingCoordinator;
    private readonly string? _documentUri;
    private readonly Action<string, string, string>? _ensureTableColumns;
    private readonly InMemorySchemaProvider? _parserSchema;
    private readonly ISqlDbWordListProvider? _wordListProvider;
    private readonly Func<string?>? _connectionNameProvider;
    private readonly Func<string?>? _databaseNameProvider;
    private SqlDialect _dialect = SqlDialect.Netezza;

    private const string FastSnippetTxt = "fast";

    public SqlCompletionProvider(SqlCodeEditor sqlCodeEditor, ISqlAutocompleteData sqlAutocompleteData,
        ISomeEditorOptions snippetsProvider, NzCompletionEngine? completionEngine = null,
        DocumentParsingCoordinator? parsingCoordinator = null, string? documentUri = null,
        Action<string, string, string>? ensureTableColumns = null,
        InMemorySchemaProvider? parserSchema = null,
        SqlDialect dialect = SqlDialect.Netezza,
        ISqlDbWordListProvider? wordListProvider = null,
        Func<string?>? connectionNameProvider = null,
        Func<string?>? databaseNameProvider = null)
    {
        _someEditorOptions = snippetsProvider;
        _snippetService = new SnippetInfoService(_someEditorOptions);
        _sqlCodeEditor = sqlCodeEditor;
        _parsingCoordinator = parsingCoordinator;
        _documentUri = documentUri;
        _ensureTableColumns = ensureTableColumns;
        _parserSchema = parserSchema;
        _wordListProvider = wordListProvider;
        _connectionNameProvider = connectionNameProvider;
        _databaseNameProvider = databaseNameProvider;
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
        if (CompletionGate.ShouldSuppressTrigger(triggerChar))
            return new CompletionResult([], null, true);

        var rawSqlText = _sqlCodeEditor.Document?.Text ?? string.Empty;
        string? lastWord = CompletionFragment.GetLastWordFromText(rawSqlText, position);
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
        bool forced = triggerChar is '.' or null;
        var orchestration = await CompletionOrchestrator.GetCompletions(
            rawSqlText,
            position,
            _parserSchema,
            _dialect,
            _wordListProvider,
            ShouldRunLegacyPath,
            _parsingCoordinator,
            _documentUri,
            _connectionNameProvider?.Invoke(),
            _databaseNameProvider?.Invoke(),
            new CompletionOrchestrationOptions
            {
                ForcedAutocomplete = forced,
                HydrateColumns = TryHydrateColumnsForDotCompletion
            });

        foreach (var ci in orchestration.EngineItems)
            completionData.Add(CompletionDataSql.FromEngineItem(ci, MapGlyph(ci.Kind)));

        string completionPrefix = lastWord ?? string.Empty;
        AddVariableCompletions(completionData, completionPrefix);
        AddSnippetCompletions(completionData, completionPrefix);

        foreach (var item in orchestration.WordListItems)
            completionData.Add(FromWordListItem(item));

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
        => CompletionGate.ShouldSuppressTrigger(triggerChar);

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

    private static CompletionDataSql FromWordListItem(SqlWordListItem item)
        => new(
            item.Label,
            item.Detail ?? item.Description ?? item.Kind.ToString(),
            false,
            MapGlyph(item.Kind),
            null,
            item.Label,
            item.Detail,
            item.Description);

    private static Glyph MapGlyph(SqlWordListKind kind) => kind switch
    {
        SqlWordListKind.Database => Glyph.Database,
        SqlWordListKind.Schema => Glyph.Schema,
        SqlWordListKind.Table => Glyph.Table,
        SqlWordListKind.View => Glyph.View,
        SqlWordListKind.Procedure => Glyph.Procedure,
        SqlWordListKind.Synonym => Glyph.Synonym,
        SqlWordListKind.ExternalTable => Glyph.ExternalTable,
        SqlWordListKind.Function => Glyph.Function,
        SqlWordListKind.Column => Glyph.Column,
        SqlWordListKind.Alias => Glyph.Table,
        SqlWordListKind.With => Glyph.WithDb,
        SqlWordListKind.TempTable => Glyph.TempTable,
        SqlWordListKind.Subquery => Glyph.SubQuery,
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

        if (_dialect == SqlDialect.Sqlite)
        {
            if (string.IsNullOrWhiteSpace(database))
                database = _databaseNameProvider?.Invoke() ?? "";
            if (string.IsNullOrWhiteSpace(schema))
                schema = "main";
        }

        if (string.IsNullOrWhiteSpace(table)) return false;
        _ensureTableColumns(database, schema, table);
        return true;
    }
}
