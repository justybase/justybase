using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.Core.Database;
using Microsoft.Extensions.DependencyInjection;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Editor;
using JustyBase.Helpers;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommons;
using JustyBase.Services;
using JustyBase.Helpers.Shared;
using JustyBase.ViewModels.Tools;
using System.Collections.ObjectModel;
using System.Text;
using System.Data;
using JustyBase.Services.Documents;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.Editor.InlineCompletion;
using JustyBase.Services.Fim;

namespace JustyBase.ViewModels.Documents;

public sealed class FileChangedInfo
{
    public required string FilePath { get; init; }
    public required string CurrentText { get; init; }
    public required string NewText { get; init; }
    public required Action ReloadAction { get; init; }
    public required Action KeepCurrentAction { get; init; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001",
    Justification = "Document cleanup is owned by ICleanableViewModel and DockableCleanupService.")]
public sealed partial class SqlDocumentViewModel : DocumentBaseVM, ISqlAutocompleteData, ICleanableViewModel, IHotDocumentVm
{
    private readonly LogToolViewModel _logToolViewModel;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly HistoryService _historyService;
    private readonly ISqlCodeFormatterService _sqlCodeFormatterService;
    private readonly ISqlVariableProcessor _sqlVariableProcessor;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISqlDocumentExecutionServices _executionServices;
    private readonly ISqlDocumentInteractionServices _interactionServices;
    private readonly ISqlDocumentUiServices _uiServices;
    private readonly ISqlResultManager _resultManager;
    private readonly INetezzaMaintenanceDialogService? _netezzaMaintenanceDialogService;
    private readonly NzLinterService? _linterService;
    private readonly NzCompletionEngine? _completionEngine;
    private readonly InMemorySchemaProvider? _parserSchema;
    private readonly DocumentParsingCoordinator? _parsingCoordinator;
    private readonly ISqlDbWordListProvider? _wordListProvider;
    private readonly FimEditorAttachment _fimAttachment;
    private readonly Queue<string> _pendingSnippetTexts = [];
    private int _cleanupStarted;
    private List<SymbolOccurrence> _lastReferenceOccurrences = [];
    private int _referenceNavigateIndex;
    private SqlDialect _documentDialect = SqlDialect.Netezza;

    public SqlDocumentViewModel(IFactory factory,
       IGeneralApplicationData generalApplicationData, HistoryService historyService,
         ISqlCodeFormatterService sqlCodeFormatterService, IMessageForUserTools messageForUserTools, ISimpleLogger simpleLogger,
         ISqlVariableProcessor sqlVariableProcessor,
          LogToolViewModel logToolViewModel,
          IDocumentCloseDecisionService documentCloseDecisionService,
          ISqlDocumentExecutionServices executionServices,
          ISqlDocumentInteractionServices interactionServices,
          ISqlDocumentUiServices uiServices,
          IActiveDocumentManager activeDocumentManager,
          ISqlResultManager resultManager,
          INetezzaMaintenanceDialogService? netezzaMaintenanceDialogService = null,
          NzLinterService? linterService = null,
          NzCompletionEngine? completionEngine = null,
          InMemorySchemaProvider? parserSchema = null,
          DocumentParsingCoordinator? parsingCoordinator = null,
          FimInlineCompletionBridge? fimBridge = null,
          ISqlDbWordListProvider? wordListProvider = null
           )
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        // SQL documents can be reordered as tabs, but never split or floated. A single document
        // host lets Dock retain each cached editor view without reparenting it.
        CanFloat = false;
        CanDrag = true;
        AllowedDockOperations = DockOperationMask.Fill;
        DockCapabilityHelper.SyncOverridesFromFlags(this);

        _generalApplicationData = generalApplicationData;
        _historyService = historyService;
        _sqlCodeFormatterService = sqlCodeFormatterService;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
        _resultManager = resultManager;
        _netezzaMaintenanceDialogService = netezzaMaintenanceDialogService;
        _linterService = linterService;
        _completionEngine = completionEngine;
        _parserSchema = parserSchema;
        _parsingCoordinator = parsingCoordinator;
        _wordListProvider = wordListProvider;
        _fimAttachment = new FimEditorAttachment(fimBridge);
        this.Factory = factory;
        _sqlVariableProcessor = sqlVariableProcessor;
        _logToolViewModel = logToolViewModel;
        _executionServices = executionServices;
        _interactionServices = interactionServices;
        _uiServices = uiServices;
        _interactionServices.LoadTextFromChangedFileAction = filePath => _uiServices.LoadTextFromChangedFile(SqlEditor, filePath);
        if (ActiveDocumentManager.ActiveSqlDocumentViewModel is null)
        {
            ActiveDocumentManager.ActiveSqlDocumentViewModel = this;
        }

        SqlDocumentViewModelHelper.SetConnectionList(_generalApplicationData, _messageForUserTools, _simpleLogger);
        RefreshConnectionList();

        WordWrap = false;


        CutCommand = new RelayCommand(() => SqlEditor?.Cut());
        CopyCommand = new RelayCommand(() => SqlEditor?.Copy());
        CopyWithFormatsCommand = new AsyncRelayCommand(CopyWithFormats);
        PasteCommand = new RelayCommand(() =>
        {
            SqlEditor?.Paste();
        });
        UndoCommand = new RelayCommand(() => SqlEditor?.Undo());
        RedoCommand = new RelayCommand(() => SqlEditor?.Redo());
        ContinueOnError = false;
        IsRunEnabled = true;
        PeriodicIntervalText = "00:00:10";
        VmSharedPreparation();
        InsertTextAction = InsertTextRequest;

        GetCurrentTextFunc = () => SqlEditor?.Text ?? string.Empty;
        GetCurrentTextDispatcherFunc = () => JustyBase.Helpers.UiThreadMarshal.InvokeAsync(() => SqlEditor?.Text ?? string.Empty);
        OnFileChangedExternalDispatcher = info => { _ = _uiServices.ShowFileDiffDialogAsync(info); };
        UiThreadInvoker = action => Dispatcher.UIThread.Post(action);
    }
    public void InsertTextRequest(object data, bool rawMode)
    {
        string textToInsert = rawMode
            ? data?.ToString() ?? string.Empty
            : StringExtension.ConvertAsSqlCompatybile(data);

        var editor = SqlEditor;
        if (editor?.Document is null)
        {
            AppendTextToColdDocumentState(textToInsert);
            return;
        }

        //SqlEditor.Focus();
        editor.TextArea?.Focus();
        editor.SelectedText = "";
        editor.Document.Insert(editor.CaretOffset, textToInsert);
    }

    [ObservableProperty]
    public partial SqlCodeEditor SqlEditor { get; set; }

    private SqlCodeEditor? _wiredSqlEditor;
    /// <summary>True after first content hydrate (disk load / offline text / empty). Tab switch must not re-load.</summary>
    private bool _contentHydrated;

    private void OnCaretPositionChanged(object? sender, EventArgs e) =>
        ActiveDocumentManager.AtCharAction?.Invoke(GetCarretInfo());

    private void OnTextAreaGotFocusResults(object? sender, RoutedEventArgs e) =>
        ActiveDocumentManager.ResultsFromActiveTab(this);

    private void OnSqlEditorGotFocus(object? sender, RoutedEventArgs e) =>
        SqlCodeEditorHelpers.LastFocusedEditor = SqlEditor;

    private void DetachSqlEditorHandlers(SqlCodeEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        if (editor.TextArea is not null)
        {
            editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            editor.TextArea.GotFocus -= OnTextAreaGotFocusResults;
            editor.TextArea.GotFocus -= OnSqlEditorGotFocus;
        }

        editor.KeyDown -= MarkTabEdited;
        editor.GotFocus -= OnSqlEditorGotFocus;
        editor.ContolShiftvAction = null!;
        editor.GoToLineAsyncAction = null!;
        editor.RenameRequested = null;
        editor.GoToDefinitionRequested = null;
        editor.FindReferencesRequested = null;
        _fimAttachment.Detach();
    }

    partial void OnSqlEditorChanged(SqlCodeEditor value)
    {
        // Dock tab reorder is Remove+Insert on VisibleDockables, which recreates SqlDocumentView
        // even with CacheDocumentTabContent. Preserve live text from the previous editor instance.
        var previous = _wiredSqlEditor;
        string? textToPreserve = null;
        int? caretToPreserve = null;
        int? selectionStartToPreserve = null;
        int? selectionLengthToPreserve = null;
        if (previous is not null && !ReferenceEquals(previous, value))
        {
            textToPreserve = previous.Text;
            caretToPreserve = previous.CaretOffset;
            selectionStartToPreserve = previous.SelectionStart;
            selectionLengthToPreserve = previous.SelectionLength;
        }

        DetachSqlEditorHandlers(previous);
        _wiredSqlEditor = value;

        // Populate text before Initialize / AttachToEditor so Document.Text assignment does not
        // fire SemanticLineColorizer.WarmCache or NzLinterService.OnTextChanged as a side effect
        // of a non-user edit (tab reorder recreates the editor control).
        var contentWasSet = false;
        if (!_contentHydrated)
        {
            HydrateEditorContent(value);
            _contentHydrated = true;
            contentWasSet = true;
        }
        else if (textToPreserve is not null)
        {
            TransferEditorContent(value, textToPreserve, caretToPreserve, selectionStartToPreserve, selectionLengthToPreserve);
            contentWasSet = true;
        }

        value.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        value.KeyDown += MarkTabEdited;
        value.TextArea.GotFocus += OnTextAreaGotFocusResults;
        value.TextArea.GotFocus += OnSqlEditorGotFocus;
        value.GotFocus += OnSqlEditorGotFocus;
        value.ContolShiftvAction = ImportFromClipboardAsync;
        value.GoToLineAsyncAction = GoToLineAsyncAction;
        value.RenameRequested = () => _ = ExecuteRenameAsync();
        value.GoToDefinitionRequested = () => NavigateSqlSymbol(definitionOnly: true);
        value.FindReferencesRequested = () => NavigateSqlSymbol(definitionOnly: false);
        _documentDialect = GetCurrentSqlDialect();
        var documentUri = $"sql-doc-{Id}";
        value.Initialize(this, _generalApplicationData, _completionEngine, _parserSchema, _parsingCoordinator, documentUri,
            ensureTableColumns: (database, schema, table) => _linterService?.EnsureTableColumns(database, schema, table),
            dialect: _documentDialect,
            wordListProvider: _wordListProvider,
            connectionNameProvider: () => SelectedConnectionName,
            databaseNameProvider: () => SelectedDatabase);
        _fimAttachment.Attach(
            value,
            () => _generalApplicationData.Config.EnableFimServer,
            () => InlineCompletionController.SnapDebounceMs(
                _generalApplicationData.Config.FimDebounceMs > 0
                    ? _generalApplicationData.Config.FimDebounceMs
                    : InlineCompletionController.DefaultDebounceMs),
            BuildFimSchemaHintProvider(documentUri));
        _completionEngine?.SetDocumentUri(documentUri);
        _linterService?.AttachToEditor(value, documentUri, _documentDialect);
        if (contentWasSet)
        {
            // One intentional pass after attach — text was set before handlers were wired.
            if (!string.IsNullOrEmpty(value.Document.Text))
            {
                SemanticLineColorizer.ScheduleUpdate(value.Document, documentUri, value.TextArea.TextView, _documentDialect);
            }

            _linterService?.ForceReanalyze(value);
        }

        _ = _linterService?.SyncSchemaFromAllConnectionsAsync();

        ApplyEditorChrome(value);
        ProcessPendingSnippetInsertions(value);

        ResetFontStyle = () => _messageForUserTools.DispatcherActionInstance(
            () => _uiServices.ResetFontInView(SqlEditor, _generalApplicationData.Config.DocumentFontName));
        ResetFontStyle.Invoke();
    }

    /// <summary>
    /// Builds the per-document FIM schema-context hook: resolved from the in-memory schema
    /// snapshot + shared parse runtime for this document's URI/dialect, gated by the
    /// FimSchemaContext setting (no database round-trips in the completion hot path).
    /// </summary>
    private Func<string, int, string?>? BuildFimSchemaHintProvider(string documentUri)
    {
        if (_parsingCoordinator is null || _parserSchema is null)
        {
            return null;
        }

        return (text, caret) =>
        {
            var config = _generalApplicationData.Config;
            if (!config.FimSchemaContext)
            {
                return null;
            }

            var maxTokens = Math.Clamp(config.FimSchemaContextMaxTokens <= 0 ? 256 : config.FimSchemaContextMaxTokens, 64, 1024);
            return FimSchemaHintBuilder.Build(
                _parsingCoordinator,
                _parserSchema,
                documentUri,
                _documentDialect,
                text,
                caret,
                maxTokens * JustyBase.Ai.Embedded.Prompting.FimPresets.ApproxCharsPerToken);
        };
    }

    private static void TransferEditorContent(
        SqlCodeEditor editor,
        string text,
        int? caretOffset,
        int? selectionStart,
        int? selectionLength)
    {
        if (!string.Equals(editor.Document.Text, text, StringComparison.Ordinal))
        {
            editor.Document.Text = text;
        }

        var length = editor.Document.TextLength;
        if (selectionStart is int start && selectionLength is int selLen)
        {
            start = Math.Clamp(start, 0, length);
            selLen = Math.Clamp(selLen, 0, length - start);
            editor.Select(start, selLen);
        }

        if (caretOffset is int caret)
        {
            editor.CaretOffset = Math.Clamp(caret, 0, length);
        }
    }

    private void HydrateEditorContent(SqlCodeEditor editor)
    {
        if (!_stopReloadFileOnSaving
            && !string.IsNullOrWhiteSpace(FilePath)
            && File.Exists(FilePath))
        {
            _interactionServices.LoadEditorFromFile(editor, FilePath, SelectConnectionFromContext);
            return;
        }

        if (_generalApplicationData.TryGetDocumentById(Id, out var offlineTabData)
            && offlineTabData.SqlText is not null)
        {
            editor.Document.Text = offlineTabData.SqlText;
        }
    }

    private void ApplyEditorChrome(SqlCodeEditor editor)
    {
        _interactionServices.ApplyEditorChrome(editor, FilePath, TxtPreview, SelectConnectionFromContext);
    }


    private void MarkTabEdited(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl || Title.EndsWith('*'))
        {
            return;
        }
        Title += "*";
    }

    private async Task<int> GoToLineAsyncAction()
    {
        var lineNumber = await _messageForUserTools.ShowAskForFileNameDialogAsync(gotoLine: true);
        _ = int.TryParse(lineNumber, out var res);
        return res;
    }

    private async Task ExecuteRenameAsync()
    {
        var editor = SqlEditor;
        if (editor?.Document is null)
            return;

        var text = editor.Document.Text;
        var offset = editor.CaretOffset;

        var renameInfo = NzRenameService.GetRenameInfo(text, offset);
        if (renameInfo is null)
            return;

        var newName = await _messageForUserTools.ShowAskForFileNameDialogAsync(isRename: true);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        if (!NzRenameService.IsValidIdentifier(newName))
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(
                $"'{newName}' is not a valid identifier.", "Invalid Name");
            return;
        }

        var newText = NzRenameService.ApplyRename(text, renameInfo, newName);
        if (newText == text)
            return;

        editor.Document.Text = newText;
    }

    private void NavigateSqlSymbol(bool definitionOnly)
    {
        var editor = SqlEditor;
        if (editor?.Document is null)
            return;

        var text = editor.Document.Text;
        var offset = editor.CaretOffset;
        var symbol = NzSymbolService.GetSymbol(text, offset);
        if (symbol is null || symbol.Occurrences.Count == 0)
            return;

        if (definitionOnly)
        {
            var definition = symbol.Occurrences.FirstOrDefault(o => o.IsDefinition)
                ?? symbol.Occurrences[0];
            MoveCaretToOffset(editor, definition.StartAbsolute);
            return;
        }

        var references = symbol.Occurrences
            .OrderBy(o => o.StartAbsolute)
            .ToList();

        if (_lastReferenceOccurrences.Count == 0
            || !ReferenceSetsEqual(_lastReferenceOccurrences, references))
        {
            _lastReferenceOccurrences = references;
            _referenceNavigateIndex = 0;
        }
        else
        {
            _referenceNavigateIndex = (_referenceNavigateIndex + 1) % references.Count;
        }

        var target = references[_referenceNavigateIndex];
        MoveCaretToOffset(editor, target.StartAbsolute);
        editor.Select(target.StartAbsolute, Math.Max(1, target.EndAbsolute - target.StartAbsolute));
    }

    private static bool ReferenceSetsEqual(
        IReadOnlyList<SymbolOccurrence> left,
        List<SymbolOccurrence> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].StartAbsolute != right[i].StartAbsolute
                || left[i].EndAbsolute != right[i].EndAbsolute)
                return false;
        }

        return true;
    }

    private static void MoveCaretToOffset(SqlCodeEditor editor, int offset)
    {
        editor.CaretOffset = Math.Clamp(offset, 0, editor.Document.TextLength);
        editor.TextArea.Caret.BringCaretToView();
    }

    private async Task CopyWithFormats()
    {
        await _uiServices.CopySelectionWithFormatsAsync(SqlEditor);
    }

    private string GetTile()
    {
        return Title;
    }

    public bool TxtPreview { get; set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; }

    public ObservableCollection<string> DatabasesList => SelectedConnectionIndex == -1 || SelectedConnectionIndex >= SqlDocumentViewModelHelper.ConnectionsList.Count
        ? [] :
        SqlDocumentViewModelHelper.ConnectionsList[SelectedConnectionIndex].DatabaseList;


    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand CopyWithFormatsCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    [RelayCommand]
    private async Task SendToAiChatAsync()
    {
        if (!_generalApplicationData.Config.EnableAiChat)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(
                "AI Chat is disabled. Enable it in Preferences → AI Chat.",
                "AI Chat");
            return;
        }

        var aiChatVm = Program.ServiceProvider?.GetService<AiChatViewModel>();
        if (aiChatVm is not null)
            await aiChatVm.SendToAiChatAsync();
    }

    [RelayCommand]
    private async Task ImportFromClipboardAsync()
    {
        var clipboard = _uiServices.GetClipboard();
        if (clipboard is null)
        if (clipboard is null)
        {
            return;
        }

        await _interactionServices.ImportFromClipboardAsync(
            clipboard,
            _generalApplicationData,
            SelectedConnectionName,
            SelectedDatabase,
            AddLogMessage,
            InsertTextRequest);
    }

    /// <summary>Runs after Dock has made this document the active tab.</summary>
    public void OnActivated()
    {
        _uiServices.FocusEditorOnSelectedTab(SqlEditor);
    }

    public override void OnSelected()
    {
        base.OnSelected();
    }

    [RelayCommand]
    private async Task AbortSqlAsync()
    {
        await _executionServices.ExecutionStateService.AbortAllAsync();
        HowManyRunning = 0;
        ReturnPhase();
    }



    [ObservableProperty]
    public partial bool RunEvery { get; set; }

    [ObservableProperty]
    public partial string PeriodicIntervalText { get; set; }

    private DispatcherTimer _periodicTimer;

    [RelayCommand]
    private void RunSqlInTimer(string? option)
    {
        EnsurePeriodicTimerCreated();
        if (RunEvery)
        {
            _periodicTimer.Interval = TimeSpan.FromSeconds(3);
            _periodicTimer.Start();
        }
        else
        {
            _periodicTimer.Stop();
        }
    }

    private void EnsurePeriodicTimerCreated()
    {
        if (_periodicTimer is not null)
        {
            return;
        }
        _periodicTimer = new DispatcherTimer();
        _periodicTimer.Tick += (_, _) =>
        {
            RunSqlCommand.Execute("Grid");
            _periodicTimer.Stop();
            if (TimeSpan.TryParse(PeriodicIntervalText, out TimeSpan ts))
            {
                _periodicTimer.Interval = ts;
                _periodicTimer.Start();
            }
            else
            {
                RunEvery = false;
                PeriodicIntervalText = "00:00:10";
            }
        };
    }

    private void PluginsDownloadInfo()
    {
        _uiServices.ToggleMainWindowEnabled();
    }

    private async Task<string?> GetPathFromUser(string? ft, string? pattern, string? defaultExtension)
    {
        return await _uiServices.PickSavePathAsync(ft, pattern, defaultExtension);
    }

    private void AddLogMesage(LogMessage logItem)
    {
        LogItems?.Add(logItem);
        _logToolViewModel.AddLog(logItem);
    }


    private async Task ExpandTo(string[] toExpandPath)
    {
        DbSchemaViewModel? dbChemaViewModel = Factory.Find(a => a is DbSchemaViewModel).FirstOrDefault() as DbSchemaViewModel;
        await dbChemaViewModel?.ExpandToNodeFull(toExpandPath);
    }

    public bool HasFileOnDisk => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    public string? FilePath
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasFileOnDisk));
            ShowInExplorerCommand.NotifyCanExecuteChanged();
            CopyFullFilePathCommand.NotifyCanExecuteChanged();
            var editor = SqlEditor;
            if (editor is null)
            {
                _interactionServices.MakeWatcher(FilePath);
                return;
            }

            if (_stopReloadFileOnSaving)
            {
                _interactionServices.MakeWatcher(FilePath);
                return;
            }

            // Path change before first hydrate (or after explicit Open resets hydration).
            if (!_contentHydrated)
            {
                HydrateEditorContent(editor);
                _contentHydrated = true;
            }

            ApplyEditorChrome(editor);
        }
    }

    private bool _stopReloadFileOnSaving;

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        string? filepath = await _uiServices.PickOpenSqlFilePathAsync();
        if (string.IsNullOrWhiteSpace(filepath))
        {
            return;
        }

        // Always open/focus via the dock manager. Never mutate this VM — HotKey may fire on a
        // cached inactive SqlDocumentView (CacheDocumentTabContent) while another tab is active.
        // Already-open paths are reused and activated; new files insert after the active tab.
        ActiveDocumentManager.AddNewDocumentFromFile([filepath]);
    }

    [RelayCommand]
    private async Task SaveFileAsync(string? option)
    {
        var editor = SqlEditor;
        if (editor is null)
        {
            return;
        }

        string? fileFullPath = FilePath;
        if (FilePath is null || option == "SaveAs")
        {
            fileFullPath = await _uiServices.PickSaveSqlFilePathAsync();
        }

        if (string.IsNullOrWhiteSpace(fileFullPath))
        {
            return;
        }

        _interactionServices.EnableRaisingEvents = false;
        try
        {
            using StreamWriter fileStream = new(fileFullPath, false, Encoding.UTF8);
            editor.Document.WriteTextTo(fileStream);
        }
        catch (Exception ex)
        {
            _simpleLogger.LogAndShowError(ex, _messageForUserTools);
        }

        Title = Path.GetFileName(fileFullPath);

        if (!string.Equals(FilePath, fileFullPath, StringComparison.Ordinal))
        {
            _stopReloadFileOnSaving = true;
            FilePath = fileFullPath;
            _stopReloadFileOnSaving = false;
        }

        if (File.Exists(fileFullPath))
        {
            _interactionServices.EnableRaisingEvents = true;
        }
    }
    public string TitleFromDocumentVm => Title;

    public void RemoveAsterixFromTitleFromDocumentVM()
    {
        if (Title?.EndsWith('*') == true && (Title.Length > 1))
        {
            Title = Title[..^1] ?? "NO TITLE FOUND !";
        }
    }

    public void DoCleanup()
    {
        _periodicTimer?.Stop();
        SharedCleanup();
    }

    public Action<FileChangedInfo>? OnFileChangedExternal
    {
        get => _interactionServices.OnFileChangedExternal;
        set => _interactionServices.OnFileChangedExternal = value;
    }
    public Action<FileChangedInfo>? OnFileChangedExternalDispatcher
    {
        get => _interactionServices.OnFileChangedExternalDispatcher;
        set => _interactionServices.OnFileChangedExternalDispatcher = value;
    }
    public Action<Action>? UiThreadInvoker
    {
        get => _interactionServices.UiThreadInvoker;
        set => _interactionServices.UiThreadInvoker = value;
    }
    public Func<string>? GetCurrentTextFunc
    {
        get => _interactionServices.GetCurrentTextFunc;
        set => _interactionServices.GetCurrentTextFunc = value;
    }
    public Func<Task<string>>? GetCurrentTextDispatcherFunc
    {
        get => _interactionServices.GetCurrentTextDispatcherFunc;
        set => _interactionServices.GetCurrentTextDispatcherFunc = value;
    }
    public ObservableCollection<ConnectionItem> ConnectionsList { get; set; } = [];
    public void RefreshConnectionList()
    {
        ConnectionsList.Clear();
        foreach (var item in SqlDocumentViewModelHelper.ConnectionsList)
        {
            ConnectionsList.Add(item);
        }
    }

    public Action<object, bool>? InsertTextAction { get; set; }

    [ObservableProperty]
    public partial bool WordWrap { get; set; }
    [ObservableProperty]
    public partial int FontSize { get; set; } = ISomeEditorOptions.DEFAULT_DOCUMENT_FONT_SIZE;

    public Action ResetFontStyle { get; set; }

    [ObservableProperty]
    public partial string SqlGroup { get; set; } = "General";

    private string GetCarretInfo()
    {
        var c = SqlEditor.TextArea.Caret;
        return $"offset {c.Offset:N0} column {c.Column} line {c.Line}  ";
    }

    public List<MenuItemForCurrentOptions> CurrentOptionsList { get; init; } = [];

    public ICommand CommentLinesCommand { get; set; }
    public ICommand RenameCommand { get; set; }

    private void VmSharedPreparation()
    {
        CommentLinesCommand = new RelayCommand(() => EditorHelpers.CommentSelectedLines(SqlEditor));
        RenameCommand = new AsyncRelayCommand(ExecuteRenameAsync);
        LogItems = [];

        if (string.IsNullOrEmpty(SelectedDatabase))
        {
            SelectedConnectionIndexAdditionalLogic(SelectedConnectionIndex);
        }

        CurrentOptionsList.Clear();
        string[] optionHeaders =
        [
            SqlDocumentViewModelHelper.CurrentOptionsListDROP,
            SqlDocumentViewModelHelper.CurrentOptionsListDDL,
            SqlDocumentViewModelHelper.CurrentOptionsListRECREATE,
            SqlDocumentViewModelHelper.CurrentOptionsListRENAME,
            SqlDocumentViewModelHelper.CurrentOptionsListJUMP_TO,
            SqlDocumentViewModelHelper.CurrentOptionsListCREATE_FROM,
            SqlDocumentViewModelHelper.CurrentOptionsListGROOM,
            SqlDocumentViewModelHelper.CurrentOptionsListSELECT
        ];
        foreach (var optionHeader in optionHeaders)
        {
            AddCurrentOption(optionHeader);
        }
    }

    private void AddCurrentOption(string optionHeader)
    {
        CurrentOptionsList.Add(new MenuItemForCurrentOptions()
        {
            OptionHeader = optionHeader,
            OptionCommand = new AsyncRelayCommand<string>(o => GetFunctionForClickedItem(o))
        });
    }

    public async Task JumpToSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListJUMP_TO);
    }
    public async Task DropSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListDROP);
    }
    public async Task RenameSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListRENAME);
    }
    public async Task GroomSelectedItem()
    {
        await RunMaintenanceWizardAsync(NetezzaMaintenanceDialogKind.Groom, SqlDocumentViewModelHelper.CurrentOptionsListGROOM);
    }
    public async Task GenerateStatsSelectedItem()
    {
        await RunMaintenanceWizardAsync(NetezzaMaintenanceDialogKind.GenerateStats, SqlDocumentViewModelHelper.CurrentOptionsListSTATS);
    }
    public async Task RecreateSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListRECREATE);
    }
    public async Task DdlSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListDDL);
    }
    public async Task CreateFromSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListCREATE_FROM);
    }
    public async Task SelectSelectedItem()
    {
        await GetFunctionForClickedItem(SqlDocumentViewModelHelper.CurrentOptionsListSELECT);
    }

    private async Task GetFunctionForClickedItem(string optionName)
    {
        string tappedWord = this.SqlEditor.GetTappedWord();

        var result = await _interactionServices.ExecuteObjectActionAsync(
            optionName, 
            tappedWord, 
            SelectedConnectionName, 
            SelectedDatabase, 
            _databaseService);

        if (result.ShowWarningNoConnection)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("Please make connection to database");
            return;
        }

        if (result.UpdatedDatabaseService != null)
        {
            _databaseService = result.UpdatedDatabaseService;
        }

        if (result.PathToExpand != null && result.PathToExpand.Length > 0)
        {
            await ExpandTo(result.PathToExpand);
        }

        if (!string.IsNullOrWhiteSpace(result.TextToInsert))
        {
            SqlEditor.InsertTextToPrevLineAndSelect(result.TextToInsert);
        }
    }

    private async Task RunMaintenanceWizardAsync(NetezzaMaintenanceDialogKind kind, string fallbackOption)
    {
        string tappedWord = SqlEditor.GetTappedWord();
        if (string.IsNullOrWhiteSpace(tappedWord))
        {
            await GetFunctionForClickedItem(fallbackOption);
            return;
        }

        if (_netezzaMaintenanceDialogService is not null)
        {
            var sql = await _netezzaMaintenanceDialogService.ShowAsync(kind, tappedWord.Trim());
            if (!string.IsNullOrWhiteSpace(sql))
            {
                SqlEditor.InsertTextToPrevLineAndSelect(sql);
            }
            return;
        }

        await GetFunctionForClickedItem(fallbackOption);
    }

    public async Task ImportFromFilePath(string path)
    {
        await _interactionServices.ImportFromFilePathAsync(
            path,
            SelectedConnectionName,
            InsertTextRequest);
    }

    public string SelectedConnectionName => SelectedConnectionIndex < 0 || SelectedConnectionIndex >= SqlDocumentViewModelHelper.ConnectionsList.Count ? "" : SqlDocumentViewModelHelper.ConnectionsList[SelectedConnectionIndex].Name;

    public bool TrySetConnection(string name)
    {
        for (int i = 0; i < SqlDocumentViewModelHelper.ConnectionsList.Count; i++)
        {
            if (SqlDocumentViewModelHelper.ConnectionsList[i].Name == name)
            {
                SelectedConnectionIndex = i;
                return true;
            }
        }
        return false;
    }

    [RelayCommand]
    private void ReplaceVariable()
    {
        SqlEditor.ReplaceVariable();
    }

    [RelayCommand]

    private async Task PasteAsInAsync(string pasteType)
    {
        string? clipboardText = await _interactionServices.GetClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return;
        }

        IsReadOnly = true;
        try
        {
            string result = _interactionServices.BuildPasteAsIn(pasteType, clipboardText.Trim());
            SqlEditor?.Document.Insert(SqlEditor.CaretOffset, result);
        }
        finally
        {
            IsReadOnly = false;
        }
    }

    [RelayCommand]
    private async Task PastClipAsSelectUnionAsync()
    {
        IsReadOnly = true;
        try
        {
            string? clipboardText = await _interactionServices.GetClipboardTextAsync();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance("clipboard is empty");
                return;
            }

            string result = _interactionServices.BuildSelectUnionFromClipboard(clipboardText);
            if (string.IsNullOrEmpty(result))
            {
                return;
            }

            SqlEditor.Document.Insert(SqlEditor.TextArea.Caret.Offset, result);
        }
        finally
        {
            IsReadOnly = false;
        }
    }

    public bool ShowDetailsButtonX => _generalApplicationData.Config.ShowDetailsButton;

    public bool IsAiChatEnabled => _generalApplicationData.Config.EnableAiChat;


    [ObservableProperty]
    public partial string SelectedDatabase { get; set; }

    private int _selectedConnectionIndex;

    public int SelectedConnectionIndex
    {
        get => _selectedConnectionIndex;
        set
        {
            if (_selectedConnectionIndex != value)
            {
                _executionServices.ConnectionManager.CloseConnection();
            }
            SelectedConnectionIndexAdditionalLogic(value);
        }
    }

    public void SelectedConnectionIndexAdditionalLogic(int value1)
    {
        if (value1 >= 0 && value1 < SqlDocumentViewModelHelper.ConnectionsList.Count)
        {
            SetProperty(ref _selectedConnectionIndex, value1, nameof(SelectedConnectionIndex));
        }
        OnPropertyChanged(nameof(DatabasesList));
        if (SelectedConnectionIndex >= 0 && SelectedConnectionIndex < SqlDocumentViewModelHelper.ConnectionsList.Count)
        {
            SelectedDatabase = SqlDocumentViewModelHelper.ConnectionsList[SelectedConnectionIndex].DefaultDatabase;
        }

        UpdateDocumentDialect();
        _ = _linterService?.SyncSchemaFromAllConnectionsAsync();
    }

    /// <summary>
    /// Resolves the SQL dialect of the currently selected connection
    /// (Db2 and SQLite documents use their dialects from JustyBase.NetezzaSql).
    /// </summary>
    private SqlDialect GetCurrentSqlDialect()
    {
        if (SelectedConnectionIndex >= 0 && SelectedConnectionIndex < ConnectionsList.Count)
        {
            return SqlDialectResolver.ForDatabaseType(ConnectionsList[SelectedConnectionIndex].DatabaseType);
        }

        return SqlDialect.Netezza;
    }

    /// <summary>
    /// Propagates a connection-change dialect switch to the editor (completion,
    /// hover, semantic coloring) and the attached linter.
    /// </summary>
    private void UpdateDocumentDialect()
    {
        SqlDialect dialect = GetCurrentSqlDialect();
        if (_documentDialect == dialect)
            return;

        _documentDialect = dialect;
        if (SqlEditor is null)
            return;

        SqlEditor.SetSqlDialect(dialect);
        _linterService?.AttachToEditor(SqlEditor, $"sql-doc-{Id}", dialect);
    }

    [ObservableProperty]
    public partial bool SingleCommand { get; set; } = false;

    [ObservableProperty]
    public partial bool ContinueOnError { get; set; }


    [ObservableProperty]
    public partial bool KeepConnectionOpen { get; set; } = true;

    partial void OnKeepConnectionOpenChanged(bool value)
    {
        if (KeepConnectionOpen) return;
        _executionServices.ConnectionManager.CloseConnection();
    }



    [ObservableProperty]
    public partial bool IsReadOnly { get; set; }

    [ObservableProperty]
    public partial bool DoPooling { get; set; }

    private object Evaluate(string expression)
    {
        object result = _sqlVariableProcessor.ReplaceSessionVariables(expression);
        try
        {
            using var tableToCompute = new DataTable();
            result = tableToCompute.Compute(expression, "");
        }
        catch (EvaluateException ex)
        {
            // Expression evaluation failed - return the variable-replaced result
            _simpleLogger.TrackError(ex, isCrash: false);
        }
        catch (SyntaxErrorException ex)
        {
            // Expression syntax error - return the variable-replaced result
            _simpleLogger.TrackError(ex, isCrash: false);
        }

        return result;
    }


    private IDatabaseService _databaseService;
    public IAsyncEnumerable<CompletionDataSql> GetWordsList(string input, Dictionary<string, List<string>> aliasDbTable,
        Dictionary<string, List<string>> subqueriesHints,
        Dictionary<string, List<string>> withs,
        Dictionary<string, List<string>> tempTables)
    {
        _sqlCodeFormatterService.SelectedConnectionName = SelectedConnectionName;
        _sqlCodeFormatterService.SelectedDatabase = SelectedDatabase;
        return _sqlCodeFormatterService.GetWordsList(input, aliasDbTable, subqueriesHints, withs, tempTables);
    }

    private void AppendTextToColdDocumentState(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!_generalApplicationData.TryGetDocumentById(Id, out var offlineTabData))
        {
            return;
        }

        try
        {
            string existingText = offlineTabData.SqlText ?? string.Empty;
            if (string.IsNullOrEmpty(existingText)
                && !string.IsNullOrWhiteSpace(offlineTabData.SqlFilePath)
                && File.Exists(offlineTabData.SqlFilePath))
            {
                existingText = File.ReadAllText(offlineTabData.SqlFilePath);
            }

            offlineTabData.SqlFilePath = null;
            offlineTabData.SqlText = string.Concat(existingText, text);
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
    }

    private static bool RequiresLiveSnippetEditor(string text)
    {
        return text.Contains("${", StringComparison.Ordinal);
    }

    private static void InsertSnippetIntoEditor(SqlCodeEditor editor, string text)
    {
        var snippet = new CodeSnippet("ABC", "DEF", text, "GHI");
        var editorSnippet = snippet.CreateAvalonEditSnippet();

        using (editor.TextArea.Document.RunUpdate())
        {
            editorSnippet.Insert(editor.TextArea);
        }
    }

    private void ProcessPendingSnippetInsertions(SqlCodeEditor editor)
    {
        while (_pendingSnippetTexts.Count > 0)
        {
            editor.CaretOffset = editor.Document.TextLength;
            InsertSnippetIntoEditor(editor, _pendingSnippetTexts.Dequeue());
        }
    }

    public void InserSnippet(string text)
    {
        var editor = SqlEditor;
        if (editor?.TextArea?.Document is null)
        {
            if (RequiresLiveSnippetEditor(text))
            {
                _pendingSnippetTexts.Enqueue(text);
            }
            else
            {
                AppendTextToColdDocumentState(text);
            }
            return;
        }

        InsertSnippetIntoEditor(editor, text);
    }

    [ObservableProperty]
    public partial ObservableCollection<LogMessage> LogItems { get; set; }

    public LogMessage AddLogMessage(string msg, LogMessageType logMessageType, DateTime dateTime, string title)
    {
        var logItem = new LogMessage(_messageForUserTools)
        {
            Timestamp = dateTime,
            Message = msg,
            Title = title,
            MessageType = logMessageType,
            Source = this.Id
        };
        AddLogMesage(logItem);
        return logItem;
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePath))]
    private void ShowInExplorer()
    {
        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            _messageForUserTools.ShowOrShowInExplorerHelper(FilePath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePath))]
    private async Task CopyFullFilePathAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        var clipboard = _uiServices.GetClipboard();
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(FilePath);
        }
    }

    private bool CanUseFilePath() => HasFileOnDisk;



    private void RefreshDatabaseList(IDatabaseService actualDatabaseService)
    {
        var syncPlan = _executionServices.DatabaseListSyncService.BuildSyncPlan(
            actualDatabaseService.GetDatabases(""),
            DatabasesList,
            SelectedDatabase);

        foreach (var databaseName in syncPlan.DatabasesToAdd)
        {
            DatabasesList.Add(databaseName);
        }

        if (!string.IsNullOrWhiteSpace(syncPlan.UpdatedSelectedDatabase)
            && !string.Equals(SelectedDatabase, syncPlan.UpdatedSelectedDatabase, StringComparison.Ordinal))
        {
            SelectedDatabase = syncPlan.UpdatedSelectedDatabase;
        }
    }


    private async Task<string?> ChoseExportPath(string option)
    {
        var spec = SqlExportPathHelper.ResolveExportSpec(option);
        return await GetPathFromUser(spec.FileTypeLabel, spec.Pattern, spec.DefaultExtension);
    }

    private void AddToHistory(
        string serviceName,
        string database,
        string commandText,
        HistoryRunStatus status,
        long durationMs,
        string? errorMessage)
    {
        try
        {
            _historyService.AddHistoryEntry(commandText, database, serviceName, status, durationMs, errorMessage);
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
    }



    private readonly char[] _variavleEndings = [' ', '\r', '\n'];
    private void SelectConnectionFromContext()
    {
        if (SqlEditor.Text.Length > 50 && SqlEditor.Text.StartsWith("--", StringComparison.Ordinal))
        {
            var startPart = SqlEditor.Text.AsSpan().Slice(2, 48);
            int index = startPart.IndexOfAny(_variavleEndings);
            if (index > 0)
            {
                ReadOnlySpan<char> word = startPart[..index];

                SelectedConnectionIndex = SqlDocumentViewModelHelper.GetConnectionIndex(word);
            }
        }
    }
    private void SharedCleanup()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        {
            return;
        }

        _generalApplicationData.RemoveDocumentById(Id);
        _interactionServices.EnableRaisingEvents = false;
        _interactionServices.Dispose();
        _fimAttachment.Dispose();

        _executionServices.ConnectionManager.EmergencyCleanup(async () => await AbortSqlAsync());
    }

    [RelayCommand]
    private void FormatSql()
    {
        IsReadOnly = true;
        try
        {
            _sqlCodeFormatterService.FormatSql(SqlEditor, _documentDialect);
        }
        finally
        {
            IsReadOnly = false;
        }
    }
    public string? TextFromDocumentVM => SqlEditor?.Text;
}
