using Dock.Model.Core;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services;

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

    Action<string>? AtCharAction { get; set; }
    Action<string>? SelectedDataGridAction { get; set; }
}
