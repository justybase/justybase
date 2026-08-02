using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.Helpers;
using JustyBase.Helpers.Interactions;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Themes;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services.Documents;

public interface ISqlDocumentUiServices
{
    IClipboard? GetClipboard();
    Task ShowFileDiffDialogAsync(FileChangedInfo info);
    void LoadTextFromChangedFile(SqlCodeEditor? editor, string? filePath);
    void FocusEditorOnSelectedTab(SqlCodeEditor? editor);
    void ResetFontInView(SqlCodeEditor? editor, string documentFontName);
    Task CopySelectionWithFormatsAsync(SqlCodeEditor? editor);
    Task<string?> PickOpenSqlFilePathAsync();
    Task<string?> PickSaveSqlFilePathAsync();
    Task<string?> PickSavePathAsync(string? fileTypeLabel, string? pattern, string? defaultExtension);
    void ToggleMainWindowEnabled();
}

public sealed class SqlDocumentUiServices : ISqlDocumentUiServices
{
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IDocumentFontService _documentFontService;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;

    public SqlDocumentUiServices(
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IDocumentFontService documentFontService,
        IMessageForUserTools messageForUserTools,
        ISimpleLogger simpleLogger)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _documentFontService = documentFontService;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
    }

    public IClipboard? GetClipboard()
    {
        return _avaloniaSpecificHelpers.GetClipboard();
    }

    public Task ShowFileDiffDialogAsync(FileChangedInfo info)
    {
        return _messageForUserTools.ShowFileDiffDialogAsync(
            info.FilePath,
            info.CurrentText,
            info.NewText,
            info.ReloadAction,
            info.KeepCurrentAction);
    }

    public void LoadTextFromChangedFile(SqlCodeEditor? editor, string? filePath)
    {
        if (editor is null || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _messageForUserTools.DispatcherActionInstance(
            () => editor.Text = File.ReadAllText(filePath),
            DispatcherPriority.MaxValue);
    }

    public void FocusEditorOnSelectedTab(SqlCodeEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => editor.TextArea?.Focus(), DispatcherPriority.Input);
    }

    public void ResetFontInView(SqlCodeEditor? editor, string documentFontName)
    {
        if (editor is null)
        {
            return;
        }

        try
        {
            var selectedFont = _documentFontService.GetFontByName(documentFontName);
            if (selectedFont is not null)
            {
                editor.FontFamily = selectedFont;
            }
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
    }

    public async Task CopySelectionWithFormatsAsync(SqlCodeEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        var clipboard = GetClipboard();
        if (clipboard is null)
        {
            return;
        }

        try
        {
            using var highlighter = new AvaloniaEdit.Highlighting.DocumentHighlighter(
                editor.Document,
                AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("SQL"));

            string baseHtmlText = AvaloniaEdit.Highlighting.HtmlClipboard.CreateHtmlFragment(
                editor.Document,
                highlighter,
                new SimpleSegment(editor.SelectionStart, editor.SelectionLength),
                new AvaloniaEdit.Highlighting.HtmlOptions(editor.TextArea.Options));

            string backgroundColor = FluentThemeManager.IsDark ? "black" : "white";
            string foregroundColor = FluentThemeManager.IsDark ? "white" : "black";
            string htmlCode =
                $"<br/><div style=\"border-radius: 5px;border: 1px dashed gray; padding: 15px; background-color:{backgroundColor};color:{foregroundColor};\">{baseHtmlText}</div><br/>";

            using var dataTransfer = new DataTransfer();
            DataFormat<byte[]> htmlFormat = DataFormat.CreateBytesPlatformFormat("HTML Format");
            dataTransfer.Add(DataTransferItem.Create(htmlFormat, CopyHtmlOrTextClipboard.GetHtmlBytes(htmlCode)));
            await clipboard.SetDataAsync(dataTransfer);
        }
        catch (Exception ex)
        {
            _simpleLogger.LogAndShowError(ex, _messageForUserTools);
        }
    }

    public async Task<string?> PickOpenSqlFilePathAsync()
    {
        var storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        var openFile = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("sql files") { Patterns = ["*.sql"] },
                    new FilePickerFileType("all files") { Patterns = ["*"] }
                ]
            });

        return openFile.Count == 0
            ? null
            : openFile[0].Path.LocalPath;
    }

    public async Task<string?> PickSaveSqlFilePathAsync()
    {
        var storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        var saveFile = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                FileTypeChoices = [new FilePickerFileType("sql files") { Patterns = ["*.sql"] }],
                DefaultExtension = "sql",
                ShowOverwritePrompt = true
            });

        return saveFile?.Path.LocalPath;
    }

    public async Task<string?> PickSavePathAsync(string? fileTypeLabel, string? pattern, string? defaultExtension)
    {
        var storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        string resolvedLabel = string.IsNullOrWhiteSpace(fileTypeLabel) ? "files" : fileTypeLabel;
        string resolvedPattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;

        var saveFile = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                FileTypeChoices = [new FilePickerFileType(resolvedLabel) { Patterns = [resolvedPattern] }],
                DefaultExtension = defaultExtension,
                ShowOverwritePrompt = true
            });

        return saveFile?.Path.LocalPath;
    }

    public void ToggleMainWindowEnabled()
    {
        var mainWindow = _avaloniaSpecificHelpers.GetMainWindow();
        if (mainWindow is not null)
        {
            mainWindow.IsEnabled = !mainWindow.IsEnabled;
        }
    }
}
