using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services;

/// <summary>
/// Factory interface for creating dockable ViewModels.
/// Replaces Service Locator (App.GetRequiredService) usage in DockFactory.
/// </summary>
public interface IDockViewModelFactory
{
    /// <summary>
    /// Creates a new SqlDocumentViewModel instance.
    /// </summary>
    SqlDocumentViewModel CreateSqlDocumentViewModel();
    
    /// <summary>
    /// Creates a new DbSchemaViewModel instance.
    /// </summary>
    DbSchemaViewModel CreateDbSchemaViewModel();
    
    /// <summary>
    /// Creates a new VariablesViewModel instance.
    /// </summary>
    VariablesViewModel CreateVariablesViewModel();
    
    /// <summary>
    /// Creates a new LogToolViewModel instance.
    /// </summary>
    LogToolViewModel CreateLogToolViewModel();
    
    /// <summary>
    /// Creates a new FileExplorerViewModel instance.
    /// </summary>
    FileExplorerViewModel CreateFileExplorerViewModel();

    /// <summary>
    /// Creates a new GitViewModel instance.
    /// </summary>
    GitViewModel CreateGitViewModel();
    
    /// <summary>
    /// Creates a new SqlResultsFastViewModel instance.
    /// </summary>
    SqlResultsFastViewModel CreateSqlResultsFastViewModel();
    
    /// <summary>
    /// Creates a new AiChatViewModel instance.
    /// </summary>
    AiChatViewModel CreateAiChatViewModel();
    
    /// <summary>
    /// Creates a new SqlResultsViewModel instance.
    /// </summary>
    SqlResultsViewModel CreateSqlResultsViewModel();
    
    /// <summary>
    /// Creates a new HistoryViewModel instance.
    /// </summary>
    HistoryViewModel CreateHistoryViewModel();
    
    /// <summary>
    /// Creates a new SettingsViewModel instance.
    /// </summary>
    SettingsViewModel CreateSettingsViewModel();
    
    /// <summary>
    /// Creates a new ImportViewModel instance.
    /// </summary>
    ImportViewModel CreateImportViewModel();

    /// <summary>
    /// Creates a new SqlDiagnosticsViewModel instance.
    /// </summary>
    SqlDiagnosticsViewModel CreateSqlDiagnosticsViewModel();

    SqlOutlineViewModel CreateSqlOutlineViewModel();

    /// <summary>
    /// Creates a new EtlViewModel instance.
    /// </summary>
    EtlViewModel CreateEtlViewModel();

    /// <summary>
    /// Creates a new GitDiffDocumentViewModel instance.
    /// </summary>
    GitDiffDocumentViewModel CreateGitDiffDocumentViewModel();

    NetezzaSessionMonitorViewModel CreateNetezzaSessionMonitorViewModel();
}
