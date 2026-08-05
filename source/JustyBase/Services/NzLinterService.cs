using JustyBase.Common.Contracts;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Editor;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Services;    /// <summary>
    /// SQL linter service — UI/editor layer that delegates all analysis to LintEngine.
    /// Uses async Task-based scheduling with proper cancellation.
    /// SyncSchemaFromAllConnections runs on a background thread, never blocking lint analysis.
    /// Exposes LintQueue, QualityRuleRegistry, and LintEngineMetrics for configuration and monitoring.
    /// </summary>
public sealed class NzLinterService : IDisposable
{
    private readonly SqlDiagnosticsViewModel _diagnosticsVm;
    private readonly SqlOutlineViewModel? _outlineVm;
    private readonly LiveMetadataSchemaProvider? _liveMetadata;
    private readonly InMemorySchemaProvider? _schemaProvider;
    private readonly DocumentParsingCoordinator _parsingCoordinator;
    private LintEngine _lintEngine;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private SqlCodeEditor? _attachedEditor;
    private CancellationTokenSource? _currentCts;
    private CancellationTokenSource? _schemaSyncCts;
    private readonly object _lock = new();
    private volatile SqlDialect _documentDialect = SqlDialect.Netezza;
    private SqlDialect _engineDialect = SqlDialect.Netezza;
    private static readonly string[] Db2LintRuleIds =
        ["DB2001", "DB2002", "DB2003", "DB2004", "DB2005", "DB2006", "DB2007", "DB2008"];
    private readonly object _schemaLock = new();
    private bool _disposed;
    private volatile bool _schemaSynced;
    private int _lastServiceCount;
    private int _lastMaxConnectedLevel;
    private int _metadataEpoch;
    private int _suppressTextChanged;
    // Document tracking
    private string _documentUri = string.Empty;

    public NzLinterService(SqlDiagnosticsViewModel diagnosticsVm,
        IDatabaseServiceResolver databaseServiceResolver,
        InMemorySchemaProvider? schemaProvider = null,
        DocumentParsingCoordinator? parsingCoordinator = null,
        SqlOutlineViewModel? outlineVm = null,
        LiveMetadataSchemaProvider? liveMetadata = null)
    {
        _diagnosticsVm = diagnosticsVm;
        _databaseServiceResolver = databaseServiceResolver;
        _outlineVm = outlineVm;
        _liveMetadata = liveMetadata;
        _schemaProvider = schemaProvider;
        _parsingCoordinator = parsingCoordinator ?? new DocumentParsingCoordinator();
        _lintEngine = new LintEngine(_parsingCoordinator.GetOrCreate("lint-default"));
        _databaseServiceResolver.SchemaCacheLoaded += OnSchemaCacheLoaded;
    }

    /// <summary>
    /// Recreates the <see cref="LintEngine"/> when the attached document's dialect changes
    /// (e.g. Db2 documents use the Db2-only rule registry and Db2 parser runtime).
    /// </summary>
    private void EnsureEngineForDialect(SqlDialect dialect)
    {
        if (_engineDialect == dialect)
            return;

        lock (_lock)
        {
            if (_engineDialect == dialect)
                return;

            LintEngine? previous = _lintEngine;
            _lintEngine = new LintEngine(dialect, _parsingCoordinator.GetOrCreate("lint-default", dialect));
            _engineDialect = dialect;
            previous?.Dispose();
        }
    }

    /// <summary>
    /// Get the LintQueue that provides priority-sorted rule access.
    /// </summary>
    public LintQueue Queue => _lintEngine.Queue;

    /// <summary>
    /// Get the QualityRuleRegistry for configuring rule severities and priorities.
    /// </summary>
    public QualityRuleRegistry Registry => _lintEngine.Registry;

    /// <summary>
    /// Get a snapshot of current performance metrics (cache hit ratio, execution times).
    /// </summary>
    public LintMetricsSnapshot Metrics => _lintEngine.Metrics;

    /// <summary>
    /// Get the underlying LintEngine (for direct access to RunCheapRules, RunFullLint, etc.).
    /// </summary>
    public LintEngine Engine => _lintEngine;

    /// <summary>
    /// Reset all performance metrics counters. Call when starting a new editing session.
    /// </summary>
    public void ResetMetrics() => _lintEngine.ResetMetrics();

    public void UpdateSchema(IEnumerable<TableInfo> tables)
    {
        if (_schemaProvider is null) return;
        lock (_schemaLock)
        {
            foreach (var t in tables)
            {
                _schemaProvider.AddTable(t);
                _liveMetadata?.MergeAndPublish(t);
            }
        }
        Interlocked.Increment(ref _metadataEpoch);
        InvalidateAttachedDocumentLintCache();
        _ = ScheduleAnalyzeAsync();
    }

    public void ClearSchema()
    {
        lock (_schemaLock)
        {
            _schemaProvider?.Clear();
            _schemaProvider?.BumpMetadataEpoch();
        }
        _schemaSynced = false;
        Interlocked.Increment(ref _metadataEpoch);
        InvalidateAttachedDocumentLintCache();
        _ = ScheduleAnalyzeAsync();
    }

    public void AttachToEditor(SqlCodeEditor editor, string? documentUri = null, SqlDialect dialect = SqlDialect.Netezza)
    {
        // Cancel any in-flight lint before swapping editors / parse sessions.
        lock (_lock)
        {
            if (_currentCts is not null)
            {
                try { _currentCts.Cancel(); } catch { /* ignore */ }
                try { _currentCts.Dispose(); } catch { /* ignore */ }
                _currentCts = null;
            }
        }

        SqlCodeEditor? oldEditor;
        lock (_lock)
        {
            bool dialectChanged = _documentDialect != dialect;
            if (!dialectChanged
                && _attachedEditor == editor
                && string.Equals(_documentUri, documentUri ?? _documentUri, StringComparison.Ordinal))
            {
                return;
            }

            oldEditor = _attachedEditor;
            _attachedEditor = editor;
            _documentDialect = dialect;
            // Stable per-document URI avoids LRU eviction/dispose of in-use parse sessions
            // when the same document gets a new editor control instance.
            _documentUri = string.IsNullOrWhiteSpace(documentUri)
                ? $"sql-editor-{editor.GetHashCode():x8}"
                : documentUri;
        }
        if (oldEditor is not null)
            oldEditor.TextChanged -= OnTextChanged;
        editor.TextChanged += OnTextChanged;
        _schemaSynced = false;
        EnsureEngineForDialect(dialect);
        _parsingCoordinator.GetOrCreate(_documentUri, dialect);

        // Reset metrics for the new editing session
        // (document caches are already isolated via unique _documentUri)
        _lintEngine.ResetMetrics();
        ApplyLintSeveritySettings();

        editor.QuickFixMenuProvider = offset =>
        {
            var sql = editor.Document?.Text ?? string.Empty;
            var result = new List<(string Header, Action Apply)>();
            foreach (var marker in editor.GetDiagnosticMarkersAtOffset(offset))
            {
                if (marker.Tag is not LintIssue issue) continue;
                var fix = NzLintCodeActions.GetQuickFix(issue, sql, _schemaProvider);
                if (fix is null) continue;
                var localFix = fix.Value;
                var localIssue = issue;
                result.Add((localFix.Description, () =>
                {
                    var current = editor.Document?.Text ?? string.Empty;
                    var applied = localFix.Apply(current);
                    if (applied == current || editor.Document is null) return;
                    Interlocked.Increment(ref _suppressTextChanged);
                    try { editor.Document.Text = applied; }
                    finally { Interlocked.Decrement(ref _suppressTextChanged); }
                    ForceReanalyze(editor);
                }));
            }
            return result;
        };

        _diagnosticsVm.NavigateToOffset = (offset, length) =>
        {
            var textLen = editor.Document.TextLength;
            if (textLen == 0 || offset < 0 || offset >= textLen) return;
            if (length <= 0) length = 1;
            if (offset + length > textLen) length = textLen - offset;
            editor.Select(offset, length);
            editor.TextArea.Caret.BringCaretToView();
        };
        _diagnosticsVm.GetCurrentSql = () => editor.Document?.Text ?? string.Empty;
        if (_outlineVm is not null)
        {
            _outlineVm.NavigateToOffset = offset =>
            {
                if (editor.Document is null || offset < 0 || offset >= editor.Document.TextLength)
                    return;
                editor.CaretOffset = offset;
                editor.TextArea.Caret.BringCaretToView();
            };
            _outlineVm.GetCurrentSql = () => editor.Document?.Text ?? string.Empty;
            _outlineVm.UpdateOutline(editor.Document?.Text ?? string.Empty);
        }
    }

    /// <summary>
    /// Applies minimal severity toggles from <see cref="JustyBase.Common.AppOptions"/> when available.
    /// </summary>
    public void ApplyLintSeveritySettings(JustyBase.Common.AppOptions? options = null)
    {
        options ??= Program.ServiceProvider?.GetService<IGeneralApplicationData>()?.Config;
        if (options is null) return;

        // Db2 documents are analyzed with the Db2-only rule registry (DB2001–DB2008).
        // The shared NZ severity options map onto the closest Db2 equivalents.
        bool isDb2 = _documentDialect == SqlDialect.Db2;
        if (isDb2)
        {
            ApplyDb2LintSeveritySettings(options);
            return;
        }

        if (!options.SqlLinterEnabled)
        {
            Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ002", RuleSeverityConfig.Off);
            Registry.SetSeverity("SQL043", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ003", RuleSeverityConfig.Off);
            Registry.SetSeverity("SQL044", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ004", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ005", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ008", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ011", RuleSeverityConfig.Off);
            Registry.SetSeverity("SQL045", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ012", RuleSeverityConfig.Off);
            Registry.SetSeverity("SQL046", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ013", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ015", RuleSeverityConfig.Off);
            Registry.SetSeverity("NZ102", RuleSeverityConfig.Off);
            return;
        }

        SetRuleSeverity("NZ001", options.LintSeverityNz001);
        SetRuleSeverity("NZ002", options.LintSeverityNz002);
        SetRuleSeverity("SQL043", options.LintSeverityNz002);
        SetRuleSeverity("NZ003", options.LintSeverityNz003);
        SetRuleSeverity("SQL044", options.LintSeverityNz003);
        SetRuleSeverity("NZ004", options.LintSeverityNz004);
        SetRuleSeverity("NZ005", options.LintSeverityNz005);
        SetRuleSeverity("NZ008", options.LintSeverityNz008);
        SetRuleSeverity("NZ011", options.LintSeverityNz011);
        SetRuleSeverity("SQL045", options.LintSeverityNz011);
        SetRuleSeverity("NZ012", options.LintSeverityNz012);
        SetRuleSeverity("SQL046", options.LintSeverityNz012);
        SetRuleSeverity("NZ013", options.LintSeverityNz013);
        SetRuleSeverity("NZ015", options.LintSeverityNz015);
        SetRuleSeverity("NZ102", options.LintSeverityNz102);
    }

    private void SetRuleSeverity(string ruleId, string severity)
    {
        var config = severity?.Trim().ToUpperInvariant() switch
        {
            "OFF" => RuleSeverityConfig.Off,
            "ERROR" => RuleSeverityConfig.Error,
            "INFORMATION" or "INFO" => RuleSeverityConfig.Information,
            "HINT" => RuleSeverityConfig.Hint,
            _ => RuleSeverityConfig.Warning
        };
        Registry.SetSeverity(ruleId, config);
    }

    /// <summary>
    /// Applies the shared linter options to the Db2-only rule registry (DB2001–DB2008).
    /// The global switch turns every Db2 rule off; otherwise the closest NZ severity
    /// options are mapped (DB2001/DB2002/DB2003 mirror NZ001/NZ002/NZ003) and the
    /// remaining rules keep their defaults.
    /// </summary>
    private void ApplyDb2LintSeveritySettings(JustyBase.Common.AppOptions options)
    {
        if (!options.SqlLinterEnabled)
        {
            foreach (string ruleId in Db2LintRuleIds)
                Registry.SetSeverity(ruleId, RuleSeverityConfig.Off);
            return;
        }

        SetRuleSeverity("DB2001", options.LintSeverityNz001); // SELECT *
        SetRuleSeverity("DB2002", options.LintSeverityNz002); // DELETE without WHERE
        SetRuleSeverity("DB2003", options.LintSeverityNz003); // UPDATE without WHERE
        // DB2004–DB2008 stay at their defaults.
    }

    /// <summary>
    /// Sync schema from all connections on a background thread — never blocks lint.
    /// Has its own CancellationTokenSource for independent cancellation.
    /// </summary>
    public Task SyncSchemaFromAllConnectionsAsync()
    {
        if (_schemaProvider is null) return Task.CompletedTask;

        // Cancel any in-flight schema sync
        lock (_lock)
        {
            _schemaSyncCts?.Cancel();
            _schemaSyncCts?.Dispose();
            _schemaSyncCts = new CancellationTokenSource();
        }

        var capturedCts = _schemaSyncCts;
        var task = Task.Run(() =>
        {
            if (capturedCts.IsCancellationRequested) return;

            var services = _databaseServiceResolver.GetCachedServices();
            if (services.Count == 0) return;

            var anyStillLoading = false;
            lock (_schemaLock)
            {
                if (_liveMetadata is not null)
                    _liveMetadata.Clear();
                else
                {
                    _schemaProvider.Clear();
                    _schemaProvider.BumpMetadataEpoch();
                }

                Interlocked.Increment(ref _metadataEpoch);
                InvalidateAttachedDocumentLintCache();

                foreach (var service in services)
                {
                    if (capturedCts.IsCancellationRequested) return;
                    try
                    {
                        if (service.ConnectedLevel < DatabaseConnectedLevel.ConnectedColumns)
                        {
                            anyStillLoading = true;
                            continue;
                        }

                        AppendServiceSchema(_schemaProvider, service, capturedCts.Token, _liveMetadata);
                    }
                    catch
                    {
                        // Skip connections that fail during schema sync
                    }
                }

                // One epoch bump for the whole sync batch (not per table).
                _liveMetadata?.PublishEpochBump();
            }

            if (!capturedCts.IsCancellationRequested)
            {
                _lastServiceCount = services.Count;
                _lastMaxConnectedLevel = services.Count > 0
                    ? services.Max(s => (int)s.ConnectedLevel)
                    : (int)DatabaseConnectedLevel.NotConnected;
                if (!anyStillLoading)
                    _schemaSynced = true;
                Interlocked.Increment(ref _metadataEpoch);
                InvalidateAttachedDocumentLintCache();
            }
        });

        return task;
    }

    internal static void AppendServiceSchema(
        InMemorySchemaProvider provider,
        IDatabaseService service,
        CancellationToken cancellationToken,
        LiveMetadataSchemaProvider? liveMetadata = null)
    {
        // Prefetch order: Databases → Schemas → Objects → (Procedures deferred) → Columns (lazy when large).
        var pending = new List<(string Database, string Schema, DatabaseObject Obj)>();
        foreach (var database in service.GetDatabases(""))
        {
            if (cancellationToken.IsCancellationRequested) return;
            foreach (var schema in service.GetSchemas(database, ""))
            {
                if (cancellationToken.IsCancellationRequested) return;
                foreach (var table in service.GetDbObjects(database, schema, "", TypeInDatabaseEnum.Table))
                    pending.Add((database, schema, table));
                foreach (var view in service.GetDbObjects(database, schema, "", TypeInDatabaseEnum.View))
                    pending.Add((database, schema, view));
                foreach (var ext in service.GetDbObjects(database, schema, "", TypeInDatabaseEnum.ExternalTable))
                    pending.Add((database, schema, ext));
            }
        }

        var deferColumns = JustyBase.Netezza.Metadata.MetadataPrefetchContract
            .ShouldDeferColumnHydration(pending.Count);

        foreach (var (database, schema, obj) in pending)
        {
            if (cancellationToken.IsCancellationRequested) return;
            AddTableFromDbObject(provider, service, database, schema, obj, deferColumns, liveMetadata);
        }
    }

    private static void AddTableFromDbObject(
        InMemorySchemaProvider provider,
        IDatabaseService service,
        string database,
        string schema,
        DatabaseObject obj,
        bool deferColumns,
        LiveMetadataSchemaProvider? liveMetadata)
    {
        ColumnInfo[] columns;
        if (deferColumns)
        {
            columns = [];
        }
        else
        {
            columns = service.GetColumns(database, schema, obj.Name, "")
                .Select(c => new ColumnInfo(c.Name, DataType: c.FullTypeName, Description: c.Desc))
                .ToArray();
        }

        var info = new TableInfo(
            obj.Name,
            schema,
            database,
            Columns: columns,
            IsView: obj.TypeInDatabase == TypeInDatabaseEnum.View,
            IsExternal: obj.TypeInDatabase == TypeInDatabaseEnum.ExternalTable);

        if (liveMetadata is not null)
            liveMetadata.MergeAndPublish(info, bumpEpoch: false);
        else
            provider.AddTable(info);
    }

    /// <summary>
    /// Lazy-hydrate columns for a table when completion needs them (≥500 object mode).
    /// </summary>
    public void EnsureTableColumns(string database, string schema, string tableName)
    {
        if (_schemaProvider is null) return;
        var existing = _schemaProvider.GetTable(database, schema, tableName);
        if (existing?.Columns is { Count: > 0 }) return;

        var services = _databaseServiceResolver.GetCachedServices();
        foreach (var service in services)
        {
            try
            {
                var columns = service.GetColumns(database, schema, tableName, "")
                    .Select(c => new ColumnInfo(c.Name, DataType: c.FullTypeName, Description: c.Desc))
                    .ToArray();
                if (columns.Length == 0) continue;

                var info = new TableInfo(tableName, schema, database, Columns: columns);
                lock (_schemaLock)
                {
                    if (_liveMetadata is not null)
                        _liveMetadata.EnsureColumns(info);
                    else
                        _schemaProvider.AddTable(info);
                }
                Interlocked.Increment(ref _metadataEpoch);
                return;
            }
            catch
            {
                // try next connection
            }
        }
    }

    /// <summary>
    /// Hydrate columns for every physical table referenced in the current SQL document.
    /// </summary>
    private void EnsureColumnsForDocument(string sql, string documentUri)
    {
        if (_schemaProvider is null || string.IsNullOrWhiteSpace(sql)) return;
        try
        {
            var parse = _parsingCoordinator.GetOrCreate(documentUri, _documentDialect).Parse(sql);
            foreach (var stmt in parse.Statements)
                EnsureColumnsForStatement(stmt);
        }
        catch
        {
            // Parse failures are reported by the linter itself
        }
    }

    private void EnsureColumnsForStatement(Statement stmt)
    {
        switch (stmt)
        {
            case SelectStatement select:
                if (select.With?.Ctes is { } ctes)
                {
                    foreach (var cte in ctes)
                        EnsureColumnsForStatement(cte.Query);
                }
                if (select.From is { } from)
                {
                    foreach (var tr in from)
                        EnsureColumnsForTableReference(tr);
                }
                if (select.CompoundSelects is { } compounds)
                {
                    foreach (var c in compounds)
                        EnsureColumnsForStatement(c);
                }
                break;
            case InsertStatement insert:
                EnsureColumnsForTableName(insert.Target);
                if (insert.SourceQuery is not null)
                    EnsureColumnsForStatement(insert.SourceQuery);
                break;
            case UpdateStatement update:
                EnsureColumnsForTableName(update.Target);
                if (update.From is { } updFrom)
                {
                    foreach (var tr in updFrom)
                        EnsureColumnsForTableReference(tr);
                }
                break;
            case DeleteStatement delete:
                EnsureColumnsForTableName(delete.Target);
                break;
            case MergeStatement merge:
                EnsureColumnsForTableName(merge.Target);
                EnsureColumnsForTableSource(merge.Source);
                break;
        }
    }

    private void EnsureColumnsForTableReference(TableReference tr)
    {
        EnsureColumnsForTableSource(tr.Source);
        if (tr.Joins is null) return;
        foreach (var join in tr.Joins)
            EnsureColumnsForTableSource(join.Source);
    }

    private void EnsureColumnsForTableSource(TableSource source)
    {
        if (source.Table is not null)
            EnsureColumnsForTableName(source.Table);
        if (source.Subquery is not null)
            EnsureColumnsForStatement(source.Subquery);
    }

    private void EnsureColumnsForTableName(TableName name)
        => EnsureTableColumns(name.Database ?? "", name.Schema ?? "", name.Name);

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (Volatile.Read(ref _suppressTextChanged) > 0) return;
        _ = ScheduleAnalyzeAsync();
    }

    public void ForceReanalyze(SqlCodeEditor? forEditor = null)
    {
        if (_disposed) return;
        _ = ScheduleAnalyzeAsync(forEditor);
    }

    /// <summary>
    /// Async scheduling with debounce (Task.Delay), cancellation, and background schema sync.
    /// </summary>
    private async Task ScheduleAnalyzeAsync(SqlCodeEditor? targetEditor = null)
    {
        if (_disposed) return;

        try
        {
            // Document and editor are Avalonia objects — must be read on the UI thread.
            // The dispatcher may already be shutting down, so cancellation is handled
            // by the catch block below as well.
            var (sql, editor, documentUri, knownLineCount) = await Dispatcher.UIThread.InvokeAsync(() =>
                targetEditor is not null
                    ? CaptureEditorStateFor(targetEditor)
                    : CaptureEditorState());
            if (string.IsNullOrEmpty(sql) || editor is null) return;

            // Cancel any in-flight lint, create new token
            CancellationToken cancellationToken;
            lock (_lock)
            {
                if (_currentCts is not null)
                {
                    _currentCts.Cancel();
                    _currentCts.Dispose();
                }
                _currentCts = new CancellationTokenSource();
                cancellationToken = _currentCts.Token;
            }

            int debounceMs = SqlPerformancePolicy.GetLintDebounceMs(sql, knownLineCount);
            if (!await TryDebounceAsync(debounceMs, cancellationToken).ConfigureAwait(false))
                return;

            if (_disposed || cancellationToken.IsCancellationRequested) return;

            var capturedSql = sql;
            var capturedEditor = editor;
            var capturedUri = documentUri;
            var epoch = Volatile.Read(ref _metadataEpoch);
            var schemaEpoch = _schemaProvider?.MetadataEpoch ?? 0;
            var capturedToken = cancellationToken;

            // Run lint on background thread — do not pass capturedToken to Task.Run;
            // cooperative cancel inside the delegate avoids unobserved OCE when superseded.
            var lintTask = Task.Run<LintResult?>(() =>
            {
                if (capturedToken.IsCancellationRequested) return null;

                // Check if schema sync is needed; if so, start it in background and re-lint later
                if (_schemaProvider is not null)
                {
                    bool needSync;
                    lock (_lock)
                    {
                        if (!_schemaSynced)
                        {
                            needSync = true;
                        }
                        else
                        {
                            var currentServices = _databaseServiceResolver.GetCachedServices();
                            var currentCount = currentServices.Count;
                            var maxLevel = currentCount > 0
                                ? currentServices.Max(s => (int)s.ConnectedLevel)
                                : (int)DatabaseConnectedLevel.NotConnected;
                            needSync = currentCount != _lastServiceCount
                                       || maxLevel > _lastMaxConnectedLevel;
                        }
                    }

                    if (needSync)
                    {
                        // Fire background sync — lint continues with current schema, re-runs on completion
                        _ = ReanalyzeAfterSchemaSyncAsync();
                    }
                }

                if (capturedToken.IsCancellationRequested) return null;

                // Lazy-hydrate columns for tables referenced in this document (deferred ≥500 mode).
                // Completion already does this on demand; lint must do it too for SQL004 accuracy
                // and to avoid stale empty column snapshots.
                EnsureColumnsForDocument(capturedSql, capturedUri);

                if (capturedToken.IsCancellationRequested) return null;

                int lineCount = SqlPerformancePolicy.ResolveLineCountForLintGate(capturedSql, knownLineCount);

                var config = new LintConfig(
                    Sql: capturedSql,
                    Schema: _schemaProvider,
                    DocumentUri: capturedUri,
                    MetadataEpoch: CombineMetadataEpoch(epoch, schemaEpoch),
                    CancellationToken: capturedToken
                );

                try
                {
                    if (SqlPerformancePolicy.ShouldSkipLint(SqlLintInvocation.Live, lineCount, capturedSql.Length))
                    {
                        return new LintResult([], 0, 0, 0, false);
                    }

                    if (SqlPerformancePolicy.ShouldRunCheapLintOnly(lineCount, capturedSql.Length))
                    {
                        var cheapIssues = _lintEngine.RunCheapRules(capturedSql);
                        cheapIssues.Add(new LintIssue(
                            "LINT001",
                            $"Document exceeds {SqlPerformancePolicy.CheapLintOnlyCharLimit:N0} characters — semantic validation skipped (cheap rules only)",
                            LintSeverity.Information,
                            0, 0, 1, 1, 1, 1));
                        return new LintResult(cheapIssues, _lintEngine.Queue.CheapRules.Count, 0, 0, false);
                    }

                    _parsingCoordinator.GetOrCreate(capturedUri, _documentDialect).Parse(capturedSql);
                    return _lintEngine.RunFullLint(config);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            });

            var result = await lintTask.ConfigureAwait(false);
            if (result is null || _disposed || cancellationToken.IsCancellationRequested)
                return;

            // Dispatch results to UI thread
            var issues = result.Value.Issues.ToList();
            issues.AddRange(SqlRiskAnalysisService.Analyze(capturedSql, DialectRuntime.DiagnosticSource(_documentDialect)));
            var metricsSnapshot = _lintEngine.Metrics;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || cancellationToken.IsCancellationRequested) return;
                if (capturedEditor.Document is null) return;
                _diagnosticsVm.UpdateDiagnostics(issues,
                    () => capturedEditor.Document.Text,
                    s =>
                    {
                        // Suppress TextChanged during a programmatic quick-fix to avoid
                        // a cascade of lint cycles that may show transient diagnostics
                        // for the partial SQL while the editor is being updated.
                        Interlocked.Increment(ref _suppressTextChanged);
                        try
                        {
                            capturedEditor.Document.Text = s;
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _suppressTextChanged);
                        }
                        // Re-lint the mutated editor (Fix / Fix-all safe), not whichever tab is attached.
                        ForceReanalyze(capturedEditor);
                    });
                _diagnosticsVm.UpdateMetrics(metricsSnapshot);
                UpdateMarkers(capturedEditor, issues);
                _outlineVm?.UpdateOutline(capturedEditor.Document.Text);
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when superseded by new analysis
        }
        catch (ObjectDisposedException)
        {
            // Expected during disposal
        }
    }

    private (string? Sql, SqlCodeEditor? Editor, string DocumentUri, int LineCount) CaptureEditorState()
    {
        lock (_lock)
        {
            if (_attachedEditor?.Document is null)
                return (null, null, string.Empty, 0);
            return (_attachedEditor.Document.Text, _attachedEditor, _documentUri, _attachedEditor.Document.LineCount);
        }
    }

    private (string? Sql, SqlCodeEditor? Editor, string DocumentUri, int LineCount) CaptureEditorStateFor(SqlCodeEditor editor)
    {
        if (editor.Document is null)
            return (null, null, string.Empty, 0);

        lock (_lock)
        {
            // Prefer the attached document URI when this is the active editor so lint caches stay stable.
            if (ReferenceEquals(_attachedEditor, editor) && !string.IsNullOrEmpty(_documentUri))
                return (editor.Document.Text, editor, _documentUri, editor.Document.LineCount);
        }

        return (editor.Document.Text, editor, $"sql-editor-{editor.GetHashCode():x8}", editor.Document.LineCount);
    }

    private void InvalidateAttachedDocumentLintCache()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_documentUri))
                _lintEngine.InvalidateDocument(_documentUri);
        }
    }

    private static int CombineMetadataEpoch(int serviceEpoch, int schemaEpoch) =>
        HashCode.Combine(serviceEpoch, schemaEpoch);

    private void OnSchemaCacheLoaded()
    {
        if (_disposed || _schemaProvider is null) return;
        _schemaSynced = false;
        Interlocked.Increment(ref _metadataEpoch);
        InvalidateAttachedDocumentLintCache();
        _ = ReanalyzeAfterSchemaSyncAsync();
    }

    private async Task ReanalyzeAfterSchemaSyncAsync()
    {
        try
        {
            await SyncSchemaFromAllConnectionsAsync().ConfigureAwait(false);
            if (_disposed) return;
            await ScheduleAnalyzeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when superseded by new analysis or disposal
        }
        catch (ObjectDisposedException)
        {
            // Expected during disposal
        }
    }

    private static void UpdateMarkers(SqlCodeEditor editor, List<LintIssue> issues)
    {
        editor.RemoveAllErrorsWarnings();
        foreach (var issue in issues)
        {
            if (issue.StartOffset < 0) continue;
            if (issue.StartOffset >= editor.Document.TextLength) continue;
            var length = Math.Min(issue.EndOffset - issue.StartOffset,
                editor.Document.TextLength - issue.StartOffset);
            if (length <= 0) length = 1;

            var tooltip = $"[{issue.RuleId}] {issue.Message}";
            if (issue.Severity == LintSeverity.Error)
                editor.SelectError(issue.StartOffset, length, tooltip, issue);
            else
                editor.SelectWarning(issue.StartOffset, length, tooltip, issue);
        }
    }

    /// <summary>
    /// Waits for debounce without throwing when the token is superseded.
    /// </summary>
    private static async Task<bool> TryDebounceAsync(int milliseconds, CancellationToken cancellationToken)
    {
        var debounceTask = Task.Delay(milliseconds);
        if (!cancellationToken.CanBeCanceled)
        {
            await debounceTask.ConfigureAwait(false);
            return true;
        }

        var cancelSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancelSignal);

        return await Task.WhenAny(debounceTask, cancelSignal.Task).ConfigureAwait(false) == debounceTask
               && !cancellationToken.IsCancellationRequested;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            _databaseServiceResolver.SchemaCacheLoaded -= OnSchemaCacheLoaded;

            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = null;

            _schemaSyncCts?.Cancel();
            _schemaSyncCts?.Dispose();
            _schemaSyncCts = null;

            if (_attachedEditor is not null)
                _attachedEditor.TextChanged -= OnTextChanged;
            _attachedEditor = null;

            _lintEngine.Dispose();
            if (!string.IsNullOrEmpty(_documentUri))
                _parsingCoordinator.Release(_documentUri);
        }
    }
}
