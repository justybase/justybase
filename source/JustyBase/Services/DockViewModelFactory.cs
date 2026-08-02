using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Services;

/// <summary>
/// Factory implementation for creating dockable ViewModels.
/// Encapsulates Service Locator pattern in a single, testable place.
/// </summary>
public sealed class DockViewModelFactory : IDockViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DockViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public SqlDocumentViewModel CreateSqlDocumentViewModel()
        => _serviceProvider.GetRequiredService<SqlDocumentViewModel>();

    public DbSchemaViewModel CreateDbSchemaViewModel()
        => _serviceProvider.GetRequiredService<DbSchemaViewModel>();

    public VariablesViewModel CreateVariablesViewModel()
        => _serviceProvider.GetRequiredService<VariablesViewModel>();

    public LogToolViewModel CreateLogToolViewModel()
        => _serviceProvider.GetRequiredService<LogToolViewModel>();

    public FileExplorerViewModel CreateFileExplorerViewModel()
        => _serviceProvider.GetRequiredService<FileExplorerViewModel>();

    public GitViewModel CreateGitViewModel()
        => _serviceProvider.GetRequiredService<GitViewModel>();

    public SqlResultsFastViewModel CreateSqlResultsFastViewModel()
        => _serviceProvider.GetRequiredService<SqlResultsFastViewModel>();

    public AiChatViewModel CreateAiChatViewModel()
        => _serviceProvider.GetRequiredService<AiChatViewModel>();

    public SqlResultsViewModel CreateSqlResultsViewModel()
        => _serviceProvider.GetRequiredService<SqlResultsViewModel>();

    public SqlDiagnosticsViewModel CreateSqlDiagnosticsViewModel()
        => _serviceProvider.GetRequiredService<SqlDiagnosticsViewModel>();

    public SqlOutlineViewModel CreateSqlOutlineViewModel()
        => _serviceProvider.GetRequiredService<SqlOutlineViewModel>();

    public HistoryViewModel CreateHistoryViewModel()
        => _serviceProvider.GetRequiredService<HistoryViewModel>();

    public SettingsViewModel CreateSettingsViewModel()
        => _serviceProvider.GetRequiredService<SettingsViewModel>();

    public ImportViewModel CreateImportViewModel()
        => _serviceProvider.GetRequiredService<ImportViewModel>();

    public EtlViewModel CreateEtlViewModel()
        => _serviceProvider.GetRequiredService<EtlViewModel>();

    public GitDiffDocumentViewModel CreateGitDiffDocumentViewModel()
        => _serviceProvider.GetRequiredService<GitDiffDocumentViewModel>();

    public NetezzaSessionMonitorViewModel CreateNetezzaSessionMonitorViewModel()
        => _serviceProvider.GetRequiredService<NetezzaSessionMonitorViewModel>();
}
