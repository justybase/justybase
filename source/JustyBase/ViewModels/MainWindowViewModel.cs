using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core.Events;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Public.Lib.Services;
using JustyBase.QuickOpen;
using JustyBase.Services;
using JustyBase.Services.Actions;
using JustyBase.Services.Documents;
using JustyBase.Services.Updates;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using JustyBase.Views.OtherDialogs;
using System.Diagnostics;

namespace JustyBase.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DockFactory _dockFactory;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMainWindowActivationService _mainWindowActivationService;
    private readonly IDockableCleanupService _dockableCleanupService;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISqlDocumentUiServices _sqlDocumentUiServices;
    private readonly IApplicationUpdateService _applicationUpdateService;
    private readonly Services.Dialogs.IQuickOpenDialogService? _quickOpenDialogs;
    private readonly Services.Dialogs.IActionPaletteService? _actionPalette;
    private readonly Services.Dialogs.IKeymapDialogService? _keymapDialogs;
    private readonly Services.Workspace.ISqlSymbolIndexService? _symbolIndex;
    private readonly ActionRegistry? _actions;
    private readonly KeymapService? _keymap;
    private readonly InMemorySchemaProvider? _schemaProvider;
    private readonly HistoryService? _historyService;
    private readonly JustyBase.Ai.Ports.IUiDispatcher? _uiDispatcher;
    private SqlDocumentViewModel? _statusDocument;
    private int _automaticUpdateCheckStarted;

    [ObservableProperty]
    public partial IRootDock? Layout { get; set; }

    public bool? AutoDownloadUpdate => _generalApplicationData?.Config?.AutoDownloadUpdate;

    [ObservableProperty]
    public partial string CharAtMessage { get; set; }

    [ObservableProperty]
    public partial string SelectedRowsCount { get; set; }

    [ObservableProperty]
    public partial string StatusDocumentName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusConnection { get; set; } = "No connection";

    [ObservableProperty]
    public partial string StatusDatabase { get; set; } = "—";

    [ObservableProperty]
    public partial string StatusEncoding { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusEol { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusCaret { get; set; } = "Ln 1, Col 1";

    [ObservableProperty]
    public partial string StatusLastQuery { get; set; } = string.Empty;

    public KeymapService? Keymap => _keymap;

    public ActionRegistry? Actions => _actions;

    public string HistoryActionToolTip => ToolTipFor(WorkspaceActions.History, "Query history");
    public string SettingsActionToolTip => ToolTipFor(WorkspaceActions.Settings, "Settings");
    public string ImportActionToolTip => ToolTipFor(WorkspaceActions.Import, "Import data");
    public string KeymapActionToolTip => ToolTipFor(WorkspaceActions.Keymap, "Edit keyboard shortcuts");

    public string? GoToEverythingShortcut => _keymap?.GetGesture(WorkspaceActions.GoToEverything);
    public string? QuickOpenShortcut => _keymap?.GetGesture(WorkspaceActions.QuickOpen);
    public string? FindActionShortcut => _keymap?.GetGesture(WorkspaceActions.FindAction);
    public string? HistoryShortcut => _keymap?.GetGesture(WorkspaceActions.History);
    public string? SettingsShortcut => _keymap?.GetGesture(WorkspaceActions.Settings);
    public string? ImportShortcut => _keymap?.GetGesture(WorkspaceActions.Import);

    private string ToolTipFor(string actionId, string fallback)
    {
        string? gesture = _keymap?.GetGesture(actionId);
        return string.IsNullOrWhiteSpace(gesture) ? fallback : $"{fallback} ({gesture})";
    }

    private void OnKeymapChanged(object? sender, EventArgs e)
    {
        if (_uiDispatcher is null || _uiDispatcher.CheckAccess())
        {
            RaiseShortcutProperties();
        }
        else
        {
            _ = _uiDispatcher.InvokeAsync(RaiseShortcutProperties);
        }
    }

    private void RaiseShortcutProperties()
    {
        OnPropertyChanged(nameof(GoToEverythingShortcut));
        OnPropertyChanged(nameof(QuickOpenShortcut));
        OnPropertyChanged(nameof(FindActionShortcut));
        OnPropertyChanged(nameof(HistoryShortcut));
        OnPropertyChanged(nameof(SettingsShortcut));
        OnPropertyChanged(nameof(ImportShortcut));
        OnPropertyChanged(nameof(HistoryActionToolTip));
        OnPropertyChanged(nameof(SettingsActionToolTip));
        OnPropertyChanged(nameof(ImportActionToolTip));
        OnPropertyChanged(nameof(KeymapActionToolTip));
    }

    [RelayCommand]
    private async Task ShowAbout()
    {
        await _messageForUserTools.ShowAboutDialogAsync();
    }

    [RelayCommand]
    private async Task ShowActionPalette()
    {
        if (_actionPalette is null)
        {
            return;
        }

        try
        {
            IdeAction? chosen = await _actionPalette.ChooseAsync(CreateActionContext());
            if (chosen is not null)
            {
                await RunActionAsync(chosen.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
    }

    [RelayCommand]
    private async Task RunActionAsync(string? actionId)
    {
        if (_actions is null || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        ActionContext context = CreateActionContext();
        IdeAction? action = _actions.Find(actionId);
        try
        {
            if (action is null)
            {
                return;
            }

            string? whyNot = action.GetWhyNot(context);
            if (whyNot is not null)
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance(
                    $"\"{action.Title}\" cannot run right now.\n\n{whyNot}",
                    "Action");
                return;
            }

            await _actions.RunAsync(actionId, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
    }

    [RelayCommand]
    private async Task ShowKeymapEditorAsync()
    {
        if (_keymapDialogs is null)
        {
            return;
        }

        try
        {
            await _keymapDialogs.ShowAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
    }

    private ActionContext CreateActionContext()
        => new(_dockFactory.ActiveSqlDocumentViewModel, Main: this);

    [RelayCommand]
    private Task CheckForUpdatesAsync()
    {
        return RunUpdateCheckAsync(manual: true);
    }

    public void StartAutomaticUpdateCheck()
    {
        if (Interlocked.Exchange(ref _automaticUpdateCheckStarted, 1) != 0)
        {
            return;
        }

        _ = RunUpdateCheckAsync(manual: false);
    }

    private async Task RunUpdateCheckAsync(bool manual)
    {
        ApplicationUpdateResult result = await _applicationUpdateService
            .CheckAndDownloadAsync(manual)
            .ConfigureAwait(false);

        switch (result.Status)
        {
            case ApplicationUpdateStatus.Downloaded:
            case ApplicationUpdateStatus.PendingRestart:
                await PromptToRestartForUpdateAsync(result).ConfigureAwait(false);
                break;

            case ApplicationUpdateStatus.NoUpdate when manual:
                _messageForUserTools.ShowSimpleMessageBoxInstance(
                    "You are using the latest available version.",
                    "Updates");
                break;

            case ApplicationUpdateStatus.Unsupported when manual:
                _messageForUserTools.ShowSimpleMessageBoxInstance(
                    "Automatic updates are available only for a Velopack-installed Windows build.",
                    "Updates");
                break;

            case ApplicationUpdateStatus.Failed when manual:
                _messageForUserTools.ShowSimpleMessageBoxInstance(
                    result.ErrorMessage ?? "The update check failed.",
                    "Updates");
                break;
        }
    }

    private async Task PromptToRestartForUpdateAsync(ApplicationUpdateResult result)
    {
        string version = result.AvailableVersion ?? "the new version";
        bool restart = await _messageForUserTools.ShowConfirmationDialogAsync(
            $"Version {version} has been downloaded. Restart JustyBase now to apply the update?",
            "Update ready").ConfigureAwait(false);
        if (!restart)
        {
            return;
        }

        _dockFactory.SaveStartupSqlAndFiles();
        _generalApplicationData.SaveConfig();

        string[] restartArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!_applicationUpdateService.ApplyPendingUpdateAndRestart(restartArgs))
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(
                "The downloaded update is no longer available. Please check for updates again.",
                "Updates");
        }
    }

    [RelayCommand]
    private void FocusFilesSearch()
    {
        _dockFactory?.ShowToolPanel("FileSearch");
        var search = _dockFactory?.Find(d => d is FileSearchViewModel)
            .OfType<FileSearchViewModel>()
            .FirstOrDefault();
        search?.RequestSearchFocus();
    }

    [RelayCommand]
    private void ShowHistory()
    {
        _dockFactory?.AddHistoryDocument();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        _dockFactory?.AddSettingsDocument();
    }

    [RelayCommand]
    private void Import()
    {
        _dockFactory?.AddImportDocument();
    }

    [RelayCommand]
    private void ShowAiChat()
    {
        if (!IsAiChatEnabled)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(
                "AI Chat is disabled. Enable it in Preferences → AI Chat.",
                "AI Chat");
            return;
        }

#if AI
        _dockFactory?.ShowAiChat();
        RefreshViewPanelStates();
#endif
    }

    /// <summary>
    /// Feature availability flag for AI Chat (reads the live master switch). When false, the
    /// AI Chat dock panel is not created, "Fix in AI Chat" entry points are blocked, and the
    /// main-menu item is hidden.
    /// </summary>
#if AI
    public bool IsAiChatEnabled => _generalApplicationData?.Config?.EnableAiChat ?? false;
#else
    public bool IsAiChatEnabled => false;
#endif

    [RelayCommand]
    private void ToggleToolPanel(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId) || _dockFactory is null)
        {
            return;
        }

        _dockFactory.ToggleToolPanel(toolId);
        RefreshViewPanelStates();
    }

    public void RefreshViewPanelStates()
    {
        if (_dockFactory is null)
        {
            return;
        }

        IsSchemaPanelVisible = _dockFactory.IsToolPanelVisible("DbSchema");
        IsOutlinePanelVisible = _dockFactory.IsToolPanelVisible("SqlOutline");
        IsVariablesPanelVisible = _dockFactory.IsToolPanelVisible("Variables");
        IsSchemaSearchPanelVisible = _dockFactory.IsToolPanelVisible("schemaSearch");
        IsFilesPanelVisible = _dockFactory.IsToolPanelVisible("File explorer");
        IsFileSearchPanelVisible = _dockFactory.IsToolPanelVisible("FileSearch");
        IsGitPanelVisible = _dockFactory.IsToolPanelVisible("Git");
        IsLogPanelVisible = _dockFactory.IsToolPanelVisible("LogTool");
        IsNzSessionsPanelVisible = _dockFactory.IsToolPanelVisible("NetezzaSessionMonitor");
        IsResultsPanelVisible = _dockFactory.IsToolPanelVisible("FastViewModel");
        IsDiagnosticsPanelVisible = _dockFactory.IsToolPanelVisible("SqlDiagnostics");
        IsAiChatPanelVisible = _dockFactory.IsToolPanelVisible("AiChat");
        AreSidePanelsVisible = _dockFactory.AreSidePanelsVisible;
    }

    [ObservableProperty]
    public partial bool IsSchemaPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsOutlinePanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsVariablesPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSchemaSearchPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsFilesPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsFileSearchPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsGitPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLogPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsNzSessionsPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsResultsPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDiagnosticsPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsAiChatPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool AreSidePanelsVisible { get; set; } = true;

    [RelayCommand]
    private void ShowEtl()
    {
        _messageForUserTools.ShowSimpleMessageBoxInstance(
            "ETL designer is not available yet. Use SQL scripts or Import for data movement.",
            "ETL");
    }

    [RelayCommand]
    private void WindowClosing()
    {
        if (_keymap is not null)
        {
            _keymap.Changed -= OnKeymapChanged;
        }

        // A screenshot run must leave the person's session and configuration untouched.
        if (JustyBase.Services.Screenshot.ScreenshotMode.IsRequested)
        {
            return;
        }

        _dockFactory.SaveStartupSqlAndFiles();
        _generalApplicationData.SaveConfig();
    }

    [RelayCommand]
    private void OpenNewTab()
    {
        _dockFactory?.AddNewDocument(null);
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        string? filepath = await _sqlDocumentUiServices.PickOpenSqlFilePathAsync();
        if (string.IsNullOrWhiteSpace(filepath))
        {
            return;
        }

        _dockFactory.AddNewDocumentFromFile([filepath]);
    }

    [RelayCommand]
    private async Task SaveActiveDocumentAsync(string? option)
    {
        var active = _dockFactory.ActiveSqlDocumentViewModel;
        if (active is null)
        {
            return;
        }

        await active.SaveFileCommand.ExecuteAsync(option);
    }

    [RelayCommand]
    private void ChangeActiveTab(string param)
    {
        _dockFactory?.NextActiveDocument(param);
    }

    [RelayCommand]
    private void ConcentrateMode()
    {
        _dockFactory?.HideOrShowSideElements();
        RefreshViewPanelStates();
    }

    [RelayCommand]
    private Task ShowQuickOpenAsync() => ShowQuickOpenCoreAsync(searchEverything: false);

    [RelayCommand]
    private Task ShowGoToEverythingAsync() => ShowQuickOpenCoreAsync(searchEverything: true);

    private async Task ShowQuickOpenCoreAsync(bool searchEverything)
    {
        try
        {
            var searchService = new QuickOpenSearchService();
            var fileExplorer = _dockFactory.Find(d => d is FileExplorerViewModel)
                .OfType<FileExplorerViewModel>()
                .FirstOrDefault();
            var git = _dockFactory.Find(d => d is GitViewModel)
                .OfType<GitViewModel>()
                .FirstOrDefault();

            IReadOnlyList<string> roots = _generalApplicationData.Config.StartsFolderPaths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray()
                ?? [];

            // SearchItem.Name is the full path; LocalPath is only a relative directory fragment.
            IReadOnlyList<string> knownFiles = fileExplorer?.GetKnownSqlFilePaths() ?? [];

            string? gitRepo = git?.HasRepository == true ? git.SelectedRepoPath : null;

            var openDocs = _dockFactory.GetOpenSqlDocuments()
                .Select(document => (
                    document.Id,
                    document.Title ?? "Untitled",
                    document.FilePath,
                    document.SqlEditor?.Text ?? string.Empty))
                .ToArray();

            var candidates = await searchService.CollectCandidatesAsync(roots, knownFiles, gitRepo, openDocs);

            Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? symbolSearch = null;
            if (_symbolIndex is not null)
            {
                _symbolIndex.StartIndexing(roots);
                foreach (var document in _dockFactory.GetOpenSqlDocuments())
                {
                    if (!string.IsNullOrWhiteSpace(document.FilePath) && document.SqlEditor is not null)
                    {
                        _symbolIndex.IndexDocument(document.FilePath, document.SqlEditor.Text);
                    }
                }

                symbolSearch = (query, cancellationToken) => Task.FromResult(
                    QuickOpenSearchService.SearchSymbols(_symbolIndex.Search(query, 50), query));
            }

            QuickOpenSearchProviders providers = BuildQuickOpenProviders(searchEverything, symbolSearch);

            // VS Code preselects the current editor selection in Ctrl+P.
            string? initialQuery = null;
            string? selected = _dockFactory.ActiveSqlDocumentViewModel?.SqlEditor?.SelectedText;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                string firstLine = selected.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                if (firstLine.Length is >= 1 and <= 60)
                {
                    initialQuery = firstLine;
                }
            }

            // F1: prefer dialog service (no Window in VM); legacy inline path for designer/tests.
            QuickOpenHit? accepted;
            if (_quickOpenDialogs is not null)
            {
                accepted = await _quickOpenDialogs.ShowAsync(candidates, initialQuery, GotoQuickOpenPosition, providers);
            }
            else
            {
                accepted = await ShowQuickOpenLegacyAsync(searchService, candidates, initialQuery, providers);
            }

            if (accepted is null)
                return;

            // Dialog teardown restores focus to the previous control and can undo dock activation
            // (same reason AddNewDocumentFromFile posts activation at Input priority).
            QuickOpenHit hitToOpen = accepted;
            if (_uiDispatcher is not null)
            {
                await _uiDispatcher.InvokeAsync(() => OpenQuickOpenHitSafe(hitToOpen));
            }
            else
            {
                Dispatcher.UIThread.Post(() => OpenQuickOpenHitSafe(hitToOpen), DispatcherPriority.Input);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quick Open failed: {ex}");
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
    }

    private QuickOpenSearchProviders BuildQuickOpenProviders(
        bool searchEverything,
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? symbolSearch)
    {
        Func<string, string?>? gestureResolver = _keymap is null ? null : _keymap.GetGesture;
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? actionSearch =
            _actions is null
                ? null
                : (query, _) => Task.FromResult(
                    QuickOpenSearchService.SearchActions(_actions.Search(query), query, gestureResolver));

        Task<IReadOnlyList<QuickOpenSchemaObject>>? schemaLoad = null;
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? schemaSearch =
            _schemaProvider is null
                ? null
                : async (query, cancellationToken) =>
                {
                    schemaLoad ??= Task.Run(BuildSchemaObjects);
                    IReadOnlyList<QuickOpenSchemaObject> objects =
                        await schemaLoad.WaitAsync(cancellationToken).ConfigureAwait(true);
                    return QuickOpenSearchService.SearchSchema(objects, query);
                };

        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? historySearch =
            _historyService is null
                ? null
                : (query, _) => Task.FromResult(
                    QuickOpenSearchService.SearchHistory(_historyService.GetRecent(300), query));

        return new QuickOpenSearchProviders(
            Symbols: symbolSearch,
            Actions: actionSearch,
            Schema: schemaSearch,
            History: historySearch,
            SearchEverything: searchEverything);
    }

    private IReadOnlyList<QuickOpenSchemaObject> BuildSchemaObjects()
    {
        if (_schemaProvider is null)
        {
            return [];
        }

        var result = new List<QuickOpenSchemaObject>(512);
        foreach (string database in _schemaProvider.GetDatabases() ?? [])
        {
            foreach (string schema in _schemaProvider.GetSchemas(database) ?? [])
            {
                foreach (var (name, kind) in _schemaProvider.GetTableNames(database, schema) ?? [])
                {
                    if (result.Count >= 4_000)
                    {
                        return result;
                    }

                    string detail = $"{database}.{schema}";
                    string qualified = Qualify(database, schema, name);
                    QuickOpenSchemaObjectKind objectKind = kind == TableKind.View
                        ? QuickOpenSchemaObjectKind.View
                        : QuickOpenSchemaObjectKind.Table;
                    result.Add(new QuickOpenSchemaObject(
                        name,
                        objectKind,
                        database,
                        schema,
                        TableName: null,
                        detail,
                        qualified));

                    TableInfo? table = _schemaProvider.GetTable(database, schema, name);
                    if (table?.Columns is not { Count: > 0 } columns)
                    {
                        continue;
                    }

                    foreach (ColumnInfo column in columns)
                    {
                        if (result.Count >= 4_000)
                        {
                            return result;
                        }

                        result.Add(new QuickOpenSchemaObject(
                            column.Name,
                            QuickOpenSchemaObjectKind.Column,
                            database,
                            schema,
                            name,
                            $"{database}.{schema}.{name}",
                            column.Name));
                    }
                }
            }
        }

        return result;
    }

    private static string Qualify(string? database, string? schema, string name)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";
        }

        return string.IsNullOrWhiteSpace(schema)
            ? $"{database}..{name}"
            : $"{database}.{schema}.{name}";
    }

    private void OpenQuickOpenHitSafe(QuickOpenHit hit)
    {
        try
        {
            OpenQuickOpenHit(hit);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quick Open open-hit failed: {ex}");
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
    }

    /// <summary>
    /// F1 legacy fallback: inline QuickOpen window (kept for designer/tests without DI).
    /// New code path uses <see cref="Services.Dialogs.IQuickOpenDialogService"/>.
    /// </summary>
    private async Task<QuickOpenHit?> ShowQuickOpenLegacyAsync(
        QuickOpenSearchService searchService,
        IReadOnlyList<QuickOpenCandidate> candidates,
        string? initialQuery,
        QuickOpenSearchProviders? providers = null)
    {
        Window owner = _avaloniaSpecificHelpers.GetMainWindow();
        QuickOpenHit? accepted = null;
        bool completed = false;
        QuickOpenWindow? dialog = null;

        var vm = new QuickOpenViewModel(
            searchService,
            candidates,
            TimeSpan.FromSeconds(10),
            closeCancel: () =>
            {
                if (completed)
                    return;
                completed = true;
                dialog?.Close(null);
            },
            closeAccept: hit =>
            {
                if (completed)
                    return;
                completed = true;
                accepted = hit;
                if (dialog is not null)
                {
                    dialog.IsAccepting = true;
                    dialog.Close(hit);
                }
            },
            initialQuery: initialQuery,
            gotoPosition: GotoQuickOpenPosition,
            symbolSearch: providers?.Symbols,
            searchEverything: providers?.SearchEverything == true,
            actionSearch: providers?.Actions,
            schemaSearch: providers?.Schema,
            historySearch: providers?.History);

        dialog = new QuickOpenWindow(vm);
        await dialog.ShowDialog(owner);
        return accepted;
    }

    private void OpenQuickOpenHit(QuickOpenHit hit)
    {
        switch (hit.Kind)
        {
            case QuickOpenHitKind.Action:
                RunQuickOpenAction(hit);
                return;
            case QuickOpenHitKind.SchemaObject:
                InsertSchemaObject(hit);
                return;
            case QuickOpenHitKind.History:
                OpenHistoryHit(hit);
                return;
        }

        SqlDocumentViewModel? document = _dockFactory.FindOpenSqlDocument(hit.DocumentId, hit.FilePath);

        if (document is null && !string.IsNullOrWhiteSpace(hit.FilePath))
        {
            string path = Path.GetFullPath(hit.FilePath);
            if (!File.Exists(path))
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance($"File not found:\n{path}", "Quick Open");
                return;
            }

            _dockFactory.AddNewDocumentFromFile([path]);
            document = _dockFactory.FindOpenSqlDocument(null, path)
                ?? _dockFactory.ActiveSqlDocumentViewModel;
        }

        if (document is null)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(
                "Could not open the selected SQL document.",
                "Quick Open");
            return;
        }

        // Always focus synchronously — do not rely only on the deferred Post inside AddNewDocumentFromFile.
        _dockFactory.FocusSqlDocument(document);

        if (hit.Kind is QuickOpenHitKind.Content or QuickOpenHitKind.Symbol
            && hit.LineNumber is int lineNumber
            && lineNumber > 0
            && hit.MatchIndex is int matchIndex
            && hit.MatchLength is int matchLength
            && matchLength > 0)
        {
            NavigateEditorToMatch(document, lineNumber, matchIndex, matchLength);
        }
        else
        {
            document.SqlEditor?.Focus();
        }
    }

    private void RunQuickOpenAction(QuickOpenHit hit)
    {
        if (_actions is null || string.IsNullOrWhiteSpace(hit.ActionId))
        {
            return;
        }

        _ = RunActionAsync(hit.ActionId);
    }

    private void InsertSchemaObject(QuickOpenHit hit)
    {
        string? insertText = hit.InsertText;
        if (string.IsNullOrWhiteSpace(insertText))
        {
            return;
        }

        SqlDocumentViewModel? document = _dockFactory.ActiveSqlDocumentViewModel;
        var editor = document?.SqlEditor;
        if (document is not null && editor?.Document is not null)
        {
            _dockFactory.FocusSqlDocument(document);
            int offset = editor.TextArea?.Caret.Offset ?? editor.Document.TextLength;
            editor.Document.Insert(offset, insertText);
            editor.Focus();
            return;
        }

        _dockFactory.AddNewDocument(insertText, forcedTitle: "Schema");
    }

    private void OpenHistoryHit(QuickOpenHit hit)
    {
        string? sql = hit.Payload ?? hit.Snippet;
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        SqlDocumentViewModel document = _dockFactory.AddNewDocument(sql, forcedTitle: "History");
        _dockFactory.FocusSqlDocument(document);
    }

    private void GotoQuickOpenPosition(QuickOpenGotoTarget target)
    {
        var document = _dockFactory.ActiveSqlDocumentViewModel;
        var editor = document?.SqlEditor;
        if (document is null || editor?.Document is null)
        {
            return;
        }

        try
        {
            _dockFactory.FocusSqlDocument(document);
            int lineNumber = Math.Clamp(target.Line, 1, editor.Document.LineCount);
            DocumentLine line = editor.Document.GetLineByNumber(lineNumber);
            int columnOffset = Math.Clamp(target.Column, 1, line.Length + 1) - 1;
            editor.Select(line.Offset + columnOffset, 0);
            editor.TextArea.Caret.BringCaretToView();
            editor.Focus();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quick Open go-to-line failed: {ex}");
        }
    }

    private static void NavigateEditorToMatch(
        SqlDocumentViewModel document,
        int lineNumber1Based,
        int matchIndex,
        int matchLength)
    {
        var editor = document.SqlEditor;
        if (editor?.Document is null)
            return;

        TextDocument textDocument = editor.Document;
        int lineNumber = Math.Clamp(lineNumber1Based, 1, textDocument.LineCount);
        DocumentLine line = textDocument.GetLineByNumber(lineNumber);
        int start = line.Offset + Math.Clamp(matchIndex, 0, line.Length);
        int length = Math.Clamp(matchLength, 0, Math.Max(0, line.EndOffset - start));
        editor.Select(start, length);
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    public MainWindowViewModel(
        DockFactory dockFactory,
        IGeneralApplicationData generalApplicationData,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IMainWindowActivationService mainWindowActivationService,
        IDockableCleanupService dockableCleanupService,
        IMessageForUserTools messageForUserTools,
        ISqlDocumentUiServices sqlDocumentUiServices,
        IApplicationUpdateService applicationUpdateService,
        Services.Dialogs.IQuickOpenDialogService? quickOpenDialogs = null,
        JustyBase.Ai.Ports.IUiDispatcher? uiDispatcher = null,
        Services.Dialogs.IActionPaletteService? actionPalette = null,
        Services.Workspace.ISqlSymbolIndexService? symbolIndex = null,
        Services.Dialogs.IKeymapDialogService? keymapDialogs = null,
        ActionRegistry? actions = null,
        KeymapService? keymap = null,
        InMemorySchemaProvider? schemaProvider = null,
        HistoryService? historyService = null)
    {
        _dockFactory = dockFactory;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _generalApplicationData = generalApplicationData;
        _mainWindowActivationService = mainWindowActivationService;
        _dockableCleanupService = dockableCleanupService;
        _messageForUserTools = messageForUserTools;
        _sqlDocumentUiServices = sqlDocumentUiServices;
        _applicationUpdateService = applicationUpdateService;
        _quickOpenDialogs = quickOpenDialogs;
        _uiDispatcher = uiDispatcher;
        _actionPalette = actionPalette;
        _symbolIndex = symbolIndex;
        _keymapDialogs = keymapDialogs;
        _actions = actions;
        _keymap = keymap;
        _schemaProvider = schemaProvider;
        _historyService = historyService;

        if (_keymap is not null)
        {
            _keymap.Changed += OnKeymapChanged;
        }

        CharAtMessage = "";
        ConfigureDockFactoryBindings();

        if (_historyService is not null)
        {
            // Force the lazy store to load once so later AddHistoryEntry calls raise HistoryChanged.
            _ = _historyService.HistoryItemsCollection;
            _historyService.HistoryChanged += OnHistoryChanged;
            RefreshLastQueryStatus();
        }

#if DEBUG
        DebugFactoryEvents(_dockFactory);
#endif

        RegisterFactoryEvents();
        InitializeLayout();
        RefreshViewPanelStates();
        OpenStartupSqlFile(Environment.GetCommandLineArgs());

        SetupPipeCommunication();
        _dockFactory.CloseOldAddNewConnection();
        AttachStatusDocument(_dockFactory.ActiveSqlDocumentViewModel);
    }

    private void ConfigureDockFactoryBindings()
    {
        _dockFactory.AtCharAction = caret =>
        {
            CharAtMessage = $"offset {caret.Offset:N0} column {caret.Column} line {caret.Line}  ";
            StatusCaret = $"Ln {caret.Line}, Col {caret.Column}";
        };
        _dockFactory.SelectedDataGridAction = selectedRows => SelectedRowsCount = selectedRows;
    }

    private void RegisterFactoryEvents()
    {
        _dockFactory.ActiveDockableChanged += HandleActiveDockableChanged;
        _dockFactory.DockableClosed += Factory_DockableClosed;
    }

    private void HandleActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs args)
    {
        if (args.Dockable is SqlDocumentViewModel viewModel)
        {
            if (viewModel.IsRecentlyFinished)
            {
                viewModel.IsRecentlyFinished = false;
            }

            _dockFactory.ActivateSqlDocument(viewModel);
            AttachStatusDocument(viewModel);
            return;
        }

        // Switching to a non-SQL document (history, settings, import...) hides the SQL context.
        // Focusing a tool panel keeps the SQL document active, so its status stays visible.
        if (args.Dockable is DocumentBaseVM)
        {
            AttachStatusDocument(null);
        }
    }

    private void AttachStatusDocument(SqlDocumentViewModel? document)
    {
        if (ReferenceEquals(_statusDocument, document))
        {
            RefreshStatusBar();
            return;
        }

        if (_statusDocument is not null)
        {
            _statusDocument.PropertyChanged -= OnStatusDocumentPropertyChanged;
        }

        _statusDocument = document;
        if (document is not null)
        {
            document.PropertyChanged += OnStatusDocumentPropertyChanged;
        }

        RefreshStatusBar();
    }

    private void OnStatusDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SqlDocumentViewModel.Title):
            case nameof(SqlDocumentViewModel.FilePath):
            case nameof(SqlDocumentViewModel.SelectedDatabase):
            case nameof(SqlDocumentViewModel.SelectedConnectionIndex):
            case nameof(SqlDocumentViewModel.FileFormat):
                RefreshStatusBar();
                break;
        }
    }

    private void RefreshStatusBar()
    {
        SqlDocumentViewModel? document = _statusDocument;
        if (document is null)
        {
            StatusDocumentName = string.Empty;
            StatusConnection = "No connection";
            StatusDatabase = "—";
            StatusEncoding = string.Empty;
            StatusEol = string.Empty;
            StatusCaret = "Ln 1, Col 1";
            return;
        }

        StatusDocumentName = document.Title?.TrimEnd('*') ?? string.Empty;
        StatusConnection = string.IsNullOrWhiteSpace(document.SelectedConnectionName)
            ? "No connection"
            : document.SelectedConnectionName;
        StatusDatabase = string.IsNullOrWhiteSpace(document.SelectedDatabase)
            ? "—"
            : document.SelectedDatabase;

        if (document.FileFormat is { } format)
        {
            StatusEncoding = FormatEncoding(format);
            StatusEol = format.LineEnding.ToString().ToUpperInvariant();
        }
        else
        {
            StatusEncoding = "UTF-8";
            StatusEol = Environment.NewLine == "\r\n" ? "CRLF" : "LF";
        }

        if (document.GetCaretPosition() is { } caret)
        {
            StatusCaret = $"Ln {caret.Line}, Col {caret.Column}";
        }
        else
        {
            StatusCaret = "Ln 1, Col 1";
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (_uiDispatcher is null || _uiDispatcher.CheckAccess())
        {
            RefreshLastQueryStatus();
        }
        else
        {
            _ = _uiDispatcher.InvokeAsync(RefreshLastQueryStatus);
        }
    }

    private void RefreshLastQueryStatus()
    {
        HistoryEntry? latest = _historyService?.GetLatest();
        if (latest is null)
        {
            StatusLastQuery = string.Empty;
            return;
        }

        string duration = latest.DurationMs is long milliseconds
            ? $"  ·  {milliseconds:N0} ms"
            : string.Empty;
        StatusLastQuery = $"Last query {latest.Date:HH:mm:ss}{duration}";
    }

    private static string FormatEncoding(JustyBase.Services.Files.TextFileFormat format)
    {
        string name = format.Encoding.CodePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            12000 => "UTF-32 LE",
            12001 => "UTF-32 BE",
            28591 => "Latin-1",
            _ => format.Encoding.WebName.ToUpperInvariant(),
        };

        return format.HasBom ? $"{name} BOM" : name;
    }

    private void Factory_DockableClosed(object sender, DockableClosedEventArgs e)
    {
        if (ReferenceEquals(e.Dockable, _statusDocument))
        {
            AttachStatusDocument(null);
        }

        _dockableCleanupService.CleanupDockable(
            e.Dockable,
            documentId => _dockFactory.SqlResultsFastViewModel.ClearFromDocument(documentId, true));
    }

    private void InitializeLayout()
    {
        Layout = _dockFactory.CreateLayout();
        if (Layout is not null)
        {
            _dockFactory.InitLayout(Layout);
            Dispatcher.UIThread.Post(_dockFactory.ActivateCurrentSqlDocument, DispatcherPriority.Loaded);
            NotifyKeychainMigrationFallbacks();
        }
    }

    /// <summary>
    /// One-time startup notice when some saved passwords could not move to the
    /// OS secret store (or could not be read back from it). Connections keep
    /// working via encrypted-file fallback; this only informs the user.
    /// Posted through the injected UI dispatcher, per the MVVM separation guard.
    /// </summary>
    private void NotifyKeychainMigrationFallbacks()
    {
        try
        {
            if (_uiDispatcher is null
                || _generalApplicationData is not GeneralApplicationData data
                || data.KeychainFallbackCount <= 0)
            {
                return;
            }

            int count = data.KeychainFallbackCount;
            _ = _uiDispatcher.InvokeAsync(() => _messageForUserTools.ShowSimpleMessageBoxInstance(
                    $"{count} saved connection(s) could not use the OS secret store, " +
                    "so their passwords stay in the encrypted credentials file. " +
                    "Connections keep working; check the log for details.",
                    "Connection passwords"))
                .ContinueWith(
                    static faulted => _ = faulted.Exception,
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"Keychain migration notice failed: {ex.Message}");
        }
    }

    private void OpenStartupSqlFile(string[] args)
    {
        _mainWindowActivationService.TryOpenStartupSqlFile(
            args,
            startupSqlFilePath => _dockFactory?.AddNewDocumentFromFile([startupSqlFilePath]));
    }

    private void SetupPipeCommunication()
    {
        PipeCommunicationService pipeComunicationService = _mainWindowActivationService.CreatePipeCommunicationService(
            Program.JbMessagePipeName,
            x => _messageForUserTools.DispatcherActionInstance(() =>
            {
                _dockFactory?.AddNewDocumentFromFile([x]);
            }),
            () => _messageForUserTools.DispatcherActionInstance(() =>
            {
                _mainWindowActivationService.RestoreMainWindow(_avaloniaSpecificHelpers);
            }),
            ex => _messageForUserTools.ShowSimpleMessageBoxInstance(ex));
        pipeComunicationService.Start();
    }

    //for designer
#pragma warning disable CS8618
    public MainWindowViewModel()
    {
    }
#pragma warning restore CS8618

#if DEBUG
    private static void DebugFactoryEvents(DockFactory factory)
    {
        factory.ActiveDockableChanged += (_, args) =>
        {
            Debug.WriteLine($"[ActiveDockableChanged] Title='{args.Dockable?.Title}'");
        };

        factory.FocusedDockableChanged += (_, args) =>
        {
            Debug.WriteLine($"[FocusedDockableChanged] Title='{args.Dockable?.Title}'");
        };

        factory.DockableAdded += (_, args) =>
        {
            Debug.WriteLine($"[DockableAdded] Title='{args.Dockable?.Title}'");
        };

        factory.DockableRemoved += (_, args) =>
        {
            Debug.WriteLine($"[DockableRemoved] Title='{args.Dockable?.Title}'");
        };

        factory.DockableClosed += (_, args) =>
        {
            Debug.WriteLine($"[DockableClosed] Title='{args.Dockable?.Title}'");
        };

        factory.DockableMoved += (_, args) =>
        {
            Debug.WriteLine($"[DockableMoved] Title='{args.Dockable?.Title}'");
        };

        factory.DockableSwapped += (_, args) =>
        {
            Debug.WriteLine($"[DockableSwapped] Title='{args.Dockable?.Title}'");
        };

        factory.DockablePinned += (_, args) =>
        {
            Debug.WriteLine($"[DockablePinned] Title='{args.Dockable?.Title}'");
        };

        factory.DockableUnpinned += (_, args) =>
        {
            Debug.WriteLine($"[DockableUnpinned] Title='{args.Dockable?.Title}'");
        };

        factory.WindowOpened += (_, args) =>
        {
            Debug.WriteLine($"[WindowOpened] Title='{args.Window?.Title}'");
        };

        factory.WindowClosed += (_, args) =>
        {
            Debug.WriteLine($"[WindowClosed] Title='{args.Window?.Title}'");
        };

        factory.WindowAdded += (_, args) =>
        {
            Debug.WriteLine($"[WindowAdded] Title='{args.Window?.Title}'");
            //factory.InsertDockable((_factory as DockFactory)._rootDock, args.Window, 0);
        };

        factory.WindowRemoved += (_, args) =>
        {
            Debug.WriteLine($"[WindowRemoved] Title='{args.Window?.Title}'");
        };

        factory.WindowMoveDragBegin += (_, args) =>
        {
            Debug.WriteLine($"[WindowMoveDragBegin] Title='{args.Window?.Title}', Cancel={args.Cancel}, X='{args.Window?.X}', Y='{args.Window?.Y}'");
        };

        factory.WindowMoveDrag += (_, args) =>
        {
            Debug.WriteLine($"[WindowMoveDrag] Title='{args.Window?.Title}', X='{args.Window?.X}', Y='{args.Window?.Y}");
        };

        factory.WindowMoveDragEnd += (_, args) =>
        {
            Debug.WriteLine($"[WindowMoveDragEnd] Title='{args.Window?.Title}', X='{args.Window?.X}', Y='{args.Window?.Y}");
        };
    }
#endif
}
