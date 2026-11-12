using Dock.Model.Core;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services;

/// <summary>The caret's position in the active editor, delivered without parsing a display string.</summary>
public sealed record CaretPositionInfo(int Offset, int Line, int Column);

public interface IActiveDocumentManager
{
    SqlDocumentViewModel? ActiveSqlDocumentViewModel { get; set; }
    bool IsActiveDockable(IDockable dockable);
    bool IsLastDocument();
    void ResultsFromActiveTab(SqlDocumentViewModel viewModel);
    void InsertTextToActiveDocument(object data, bool rawMode);
    void AddNewDocumentFromFile(IEnumerable<string> files);
    SqlDocumentViewModel? FindOpenSqlDocument(string? documentId, string? filePath);
    void FocusSqlDocument(SqlDocumentViewModel document);
    SqlDocumentViewModel AddNewDocument(string? initText = null, bool txtPreview = false, string? forcedTitle = null);
    void InsertSnippetTextToActiveDocument(string text, string connectionName);
    SqlDocumentViewModel AddNewDocumentFromTxtPreview(string path);
    List<SqlResultsViewModel> GetDocumentResults(SqlDocumentViewModel viewModel);

    /// <summary>
    /// Opens (or focuses) the Import document and optionally pre-fills connection/database/schema/table.
    /// </summary>
    void OpenImportDocument(string? connectionName = null, string? database = null, string? schema = null, string? table = null);

    /// <summary>
    /// Opens the Import document in quick-import mode for the given source and runs it to
    /// completion; completes when the import finishes.
    /// </summary>
    Task StartQuickImportAsync(string sourcePath, string? connectionName, string? database);

    /// <summary>
    /// Opens the Import document seeded with tab-separated clipboard text without starting
    /// the import; the user reviews the detected columns and destination first.
    /// </summary>
    Task OpenImportDocumentFromPastedTextAsync(string text, string? connectionName, string? database);

    Action<CaretPositionInfo>? AtCharAction { get; set; }
    Action<string>? SelectedDataGridAction { get; set; }
}
