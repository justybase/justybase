using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Editor;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services.Documents;

public interface ISqlDocumentInteractionServices : IDisposable
{
    Action<FileChangedInfo>? OnFileChangedExternal { get; set; }
    Action<FileChangedInfo>? OnFileChangedExternalDispatcher { get; set; }
    Func<string>? GetCurrentTextFunc { get; set; }
    Func<Task<string>>? GetCurrentTextDispatcherFunc { get; set; }
    Action<Action>? UiThreadInvoker { get; set; }
    Action<string>? LoadTextFromChangedFileAction { get; set; }
    bool EnableRaisingEvents { get; set; }

    void MakeWatcher(string? path);
    /// <summary>Highlighting, folding setup, file watcher — never loads disk content.</summary>
    void ApplyEditorChrome(SqlCodeEditor editor, string? filePath, bool txtPreview, Action selectConnectionFromContext);
    /// <summary>Loads editor text from disk once (plus foldings after load).</summary>
    void LoadEditorFromFile(SqlCodeEditor editor, string filePath, Action selectConnectionFromContext);
    Task ImportFromClipboardAsync(
        IClipboard clipboard,
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        string? selectedDatabase,
        Func<string, LogMessageType, DateTime, string, LogMessage?> addLogMessage,
        Action<object, bool> insertTextAction);
    Task ImportFromFilePathAsync(
        string path,
        string connectionName,
        Action<object, bool> insertTextAction);
    Task<string> GetClipboardTextAsync();
    string BuildPasteAsIn(string pasteType, string clipboardText);
    string BuildSelectUnionFromClipboard(string clipboardText);
    Task<DbObjectActionResult> ExecuteObjectActionAsync(
        string optionName,
        string tappedWord,
        string selectedConnectionName,
        string selectedDatabase,
        IDatabaseService? currentDatabaseService);
}

public sealed class SqlDocumentInteractionServices : ISqlDocumentInteractionServices
{
    private readonly IClipboardService _clipboardService;
    private readonly ISqlImportService _sqlImportService;
    private readonly ISqlFileWatcherService _sqlFileWatcherService;
    private readonly ISqlExportOperations _sqlExportOperations;
    private readonly IDbObjectActionService _dbObjectActionService;

    public SqlDocumentInteractionServices(
        IClipboardService clipboardService,
        ISqlImportService sqlImportService,
        ISqlFileWatcherService sqlFileWatcherService,
        ISqlExportOperations sqlExportOperations,
        IDbObjectActionService dbObjectActionService)
    {
        _clipboardService = clipboardService;
        _sqlImportService = sqlImportService;
        _sqlFileWatcherService = sqlFileWatcherService;
        _sqlExportOperations = sqlExportOperations;
        _dbObjectActionService = dbObjectActionService;
    }

    public Action<FileChangedInfo>? OnFileChangedExternal
    {
        get => _sqlFileWatcherService.OnFileChangedExternal;
        set => _sqlFileWatcherService.OnFileChangedExternal = value;
    }

    public Action<FileChangedInfo>? OnFileChangedExternalDispatcher
    {
        get => _sqlFileWatcherService.OnFileChangedExternalDispatcher;
        set => _sqlFileWatcherService.OnFileChangedExternalDispatcher = value;
    }

    public Func<string>? GetCurrentTextFunc
    {
        get => _sqlFileWatcherService.GetCurrentTextFunc;
        set => _sqlFileWatcherService.GetCurrentTextFunc = value;
    }

    public Func<Task<string>>? GetCurrentTextDispatcherFunc
    {
        get => _sqlFileWatcherService.GetCurrentTextDispatcherFunc;
        set => _sqlFileWatcherService.GetCurrentTextDispatcherFunc = value;
    }

    public Action<Action>? UiThreadInvoker
    {
        get => _sqlFileWatcherService.UiThreadInvoker;
        set => _sqlFileWatcherService.UiThreadInvoker = value;
    }

    public Action<string>? LoadTextFromChangedFileAction
    {
        get => _sqlFileWatcherService.LoadTextFromChangedFileAction;
        set => _sqlFileWatcherService.LoadTextFromChangedFileAction = value;
    }

    public bool EnableRaisingEvents
    {
        get => _sqlFileWatcherService.EnableRaisingEvents;
        set => _sqlFileWatcherService.EnableRaisingEvents = value;
    }

    public void MakeWatcher(string? path)
    {
        _sqlFileWatcherService.MakeWatcher(path ?? string.Empty);
    }

    public void ApplyEditorChrome(SqlCodeEditor editor, string? filePath, bool txtPreview, Action selectConnectionFromContext)
    {
        EnableRaisingEvents = false;

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            string fileExtension = Path.GetExtension(filePath).TrimStart('.');
            string highlightingName = string.IsNullOrWhiteSpace(fileExtension)
                ? "SQL"
                : fileExtension.ToUpperInvariant();

            editor.SyntaxHighlighting =
                AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition(highlightingName)
                ?? AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("SQL");

            editor.FoldingSetup();
            selectConnectionFromContext();
        }
        else if (txtPreview)
        {
            editor.SyntaxHighlighting = AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("TXT");
        }
        else
        {
            editor.SyntaxHighlighting = AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("SQL");
            editor.FoldingSetup();
        }

        MakeWatcher(filePath);
    }

    public void LoadEditorFromFile(SqlCodeEditor editor, string filePath, Action selectConnectionFromContext)
    {
        EnableRaisingEvents = false;

        using FileStream fileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        editor.Load(fileStream);

        editor.FoldingSetup();
        editor.ForceUpdateFoldings();
        editor.CollapseFoldings();
        selectConnectionFromContext();
    }

    public Task ImportFromClipboardAsync(
        IClipboard clipboard,
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        string? selectedDatabase,
        Func<string, LogMessageType, DateTime, string, LogMessage?> addLogMessage,
        Action<object, bool> insertTextAction)
    {
        return _sqlImportService.ImportFromClipboardAsync(
            _clipboardService,
            clipboard,
            generalApplicationData,
            connectionName,
            selectedDatabase,
            addLogMessage,
            insertTextAction);
    }

    public Task ImportFromFilePathAsync(
        string path,
        string connectionName,
        Action<object, bool> insertTextAction)
    {
        return _sqlImportService.ImportFromFilePathAsync(
            path,
            connectionName,
            insertTextAction);
    }

    public Task<string> GetClipboardTextAsync()
    {
        return _clipboardService.GetTextAsync();
    }

    public string BuildPasteAsIn(string pasteType, string clipboardText)
    {
        return _sqlExportOperations.BuildPasteAsIn(pasteType, clipboardText);
    }

    public string BuildSelectUnionFromClipboard(string clipboardText)
    {
        return _sqlExportOperations.BuildSelectUnionFromClipboard(clipboardText);
    }

    public Task<DbObjectActionResult> ExecuteObjectActionAsync(
        string optionName,
        string tappedWord,
        string selectedConnectionName,
        string selectedDatabase,
        IDatabaseService? currentDatabaseService)
    {
        return _dbObjectActionService.ExecuteObjectActionAsync(
            optionName,
            tappedWord,
            selectedConnectionName,
            selectedDatabase,
            currentDatabaseService);
    }

    public void Dispose()
    {
        _sqlFileWatcherService.Dispose();
    }
}
