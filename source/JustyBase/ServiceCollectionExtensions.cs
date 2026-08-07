using Dock.Model.Core;
using JustyBase.Ai.Chat;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Helpers;
using JustyBase.Common.Services;
using JustyBase.Core.Database;
using JustyBase.Helpers;
using JustyBase.Helpers.Interactions;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services;
using JustyBase.Services.Ai;
using JustyBase.Services.Docking;
using JustyBase.Services.DataGrid;
using JustyBase.Services.Documents;
using JustyBase.Services.Embedded;
using JustyBase.Ai.Git;
using JustyBase.Services.Logging;
using JustyBase.Themes;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;
using Microsoft.Extensions.DependencyInjection;
using SqlEditor.Avalonia.AvaloniaSpecificHelpers;
using HostSimpleLogger = JustyBase.PluginCommon.Contracts.ISimpleLogger;

namespace JustyBase;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<IEncryptionHelper, WindowsLinuxEncryptionHelper>();
        collection.AddSingleton<IThemeManager, FluentThemeManager>();
        collection.AddSingleton<IOtherHelpers, OtherHelpers>();
        collection.AddSingleton<HostSimpleLogger>(sp =>
            new FileSimpleLogger(
                IGeneralApplicationData.LogsPath,
                openMessagesInNotepad: true,
                isEnabled: () =>
                {
                    try
                    {
                        return sp.GetService<IGeneralApplicationData>()?.Config.EnableFileLogging == true;
                    }
                    catch
                    {
                        return false;
                    }
                }));
        collection.AddSingleton<INetezzaMaintenanceDialogService, NetezzaMaintenanceDialogService>();
        collection.AddSingleton<IMessageForUserTools, MessageForUserTools>();
        collection.AddSingleton<IDocumentCloseDecisionService, DocumentCloseDecisionService>();
        collection.AddSingleton<IDockViewModelFactory, DockViewModelFactory>();
        collection.AddTransient<ISnippetEditorService, SnippetEditorService>();
        collection.AddTransient<SnippetControlViewModel>();
        collection.AddSingleton<IGeneralApplicationData, GeneralApplicationData>();
        collection.AddSingleton<IAvaloniaSpecificHelpers, AvaloniaSpecificHelpers>();
        collection.AddSingleton<IMainWindowActivationService, MainWindowActivationService>();
        collection.AddSingleton<IDockableCleanupService, DockableCleanupService>();
        collection.AddSingleton<IDockDocumentActivationService, DockDocumentActivationService>();
        collection.AddSingleton<IDockSessionPersistenceService, DockSessionPersistenceService>();
        collection.AddSingleton<IDockSqlDocumentFactory, DockSqlDocumentFactory>();
        collection.AddSingleton<IDockResultRoutingService, DockResultRoutingService>();
        collection.AddSingleton<IDockFileOpenService, DockFileOpenService>();
        collection.AddSingleton<IProgramErrorHandlingService, ProgramErrorHandlingService>();
        collection.AddSingleton<IDocumentFontService, DocumentFontService>();
        collection.AddSingleton<IClipboardService, ClipboardService>();
        collection.AddSingleton<ISearchInFiles, SearchInFiles>();
        collection.AddSingleton<AutocompleteService>();
        collection.AddSingleton<ISqlDbWordListProvider>(sp =>
        {
            var generalData = sp.GetRequiredService<IGeneralApplicationData>();
            return new DbWordListProvider(
                sp.GetRequiredService<AutocompleteService>(),
                connectionName =>
                {
                    try
                    {
                        return DatabaseServiceHelpers.GetDatabaseService(generalData, connectionName);
                    }
                    catch
                    {
                        // Unknown connection / driver not loaded — treat as no word list.
                        return null;
                    }
                });
        });
        collection.AddSingleton<DocumentParsingCoordinator>();
        collection.AddSingleton<NzLinterService>();
        collection.AddSingleton<SqlDiagnosticsViewModel>();
        collection.AddSingleton<SqlOutlineViewModel>();
        collection.AddSingleton<NetezzaMetadataCache>();
        collection.AddSingleton<LiveMetadataSchemaProvider>(sp => new LiveMetadataSchemaProvider(
            sp.GetRequiredService<NetezzaMetadataCache>(),
            sp.GetRequiredService<InMemorySchemaProvider>()));
        collection.AddSingleton<NetezzaSessionMonitorService>();
        collection.AddSingleton<NetezzaSessionMonitorViewModel>();
        collection.AddSingleton<SqlResultSessionStore>();
        collection.AddSingleton<NzCompletionEngine>(sp =>
        {
            var schema = sp.GetRequiredService<ISchemaProvider>();
            var coordinator = sp.GetRequiredService<DocumentParsingCoordinator>();
            return new NzCompletionEngine(schema, coordinator);
        });
        collection.AddSingleton<NzSemanticTokenClassifier>(sp =>
        {
            var schema = sp.GetRequiredService<ISchemaProvider>();
            var coordinator = sp.GetRequiredService<DocumentParsingCoordinator>();
            return new NzSemanticTokenClassifier(schema, coordinator);
        });
        collection.AddSingleton<InMemorySchemaProvider>();
        collection.AddSingleton<ISchemaProvider>(sp => sp.GetRequiredService<InMemorySchemaProvider>());
        collection.AddSingleton<AboutViewModel>();
        collection.AddSingleton<HistoryService>();
        collection.AddSingleton<DockFactory>();
        collection.AddSingleton<IFactory>(sp => sp.GetRequiredService<DockFactory>());
        collection.AddSingleton<IActiveDocumentManager>(sp => (IActiveDocumentManager)sp.GetRequiredService<DockFactory>());
        collection.AddSingleton<IGitDiffPresentationService>(sp => (IGitDiffPresentationService)sp.GetRequiredService<DockFactory>());
        collection.AddSingleton<ISqlResultManager>(sp => (ISqlResultManager)sp.GetRequiredService<DockFactory>());
        collection.AddSingleton<VariablesViewModel>();
        collection.AddSingleton<ImportViewModel>();
        collection.AddSingleton<LogToolViewModel>();
        collection.AddTransient<SettingsViewModel>();
        collection.AddTransient<MainWindowViewModel>();
        collection.AddTransient<FileExplorerViewModel>();
        collection.AddSingleton<JustyBase.Core.Git.IGitService, JustyBase.Core.Git.SystemGitService>();
        collection.AddSingleton<JustyBase.Ai.Git.IGitCommitMessageAiService>(sp =>
        {
            var settingsStore = sp.GetRequiredService<JustyBase.Ai.Embedded.Settings.IFimSettingsStore>();
            if (!settingsStore.Settings.EnableFimAi)
            {
                return new JustyBase.Ai.Git.UnavailableGitCommitMessageAiService();
            }

            var store = sp.GetRequiredKeyedService<JustyBase.Ai.Embedded.Download.IModelStore>(
                JustyBase.Services.Embedded.EmbeddedAiServiceCollectionExtensions.FimStoreKey);
            return new JustyBase.Ai.Git.LlamaServerGitCommitMessageAiService(
                sp.GetRequiredService<JustyBase.Ai.Embedded.Server.LlamaServerManager>(),
                store,
                settingsStore);
        });
        collection.AddTransient<GitViewModel>();
        collection.AddTransient<GitDiffDocumentViewModel>();
        collection.AddTransient<SqlResultsFastViewModel>();
        collection.AddTransient<DbSchemaViewModel>();
        collection.AddTransient<AddNewConnectionViewModel>();
        collection.AddTransient<SqlDocumentViewModel>();
        collection.AddTransient<SqlResultsViewModel>();
        collection.AddTransient<HistoryViewModel>();
        collection.AddSingleton<AiChatViewModel>();
        collection.AddSingleton<IChatSettingsStore, AppOptionsChatSettingsStore>();
        collection.AddSingleton<JustyBase.Ai.Embedded.Settings.IFimSettingsStore, AppOptionsFimSettingsStore>();
        collection.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        collection.AddSingleton<IChatDatabaseAccessProvider, ChatDatabaseAccessProvider>();
        collection.AddSingleton<ISqlDiagnosticsProvider>(sp =>
            new SqlDiagnosticsProviderAdapter(sp.GetRequiredService<SqlDiagnosticsViewModel>()));
        collection.AddSingleton<JustyBase.Ai.Ports.ISimpleLogger>(sp =>
            new ChatLoggerAdapter(sp.GetRequiredService<JustyBase.PluginCommon.Contracts.ISimpleLogger>()));
        collection.AddSingleton<IChatEnvironment, ChatEnvironmentAdapter>();
        collection.AddSingleton<OpenAiCompatibleChatBackend>();
        collection.AddSingleton<ILocalChatBackend>(sp => sp.GetRequiredService<OpenAiCompatibleChatBackend>());
        collection.AddSingleton<LocalChatClientFactory>();
        collection.AddSingleton<ILocalStateProvider, LocalStateProvider>();
        collection.AddSingleton<ILocalModelConfigurationService, LocalModelConfigurationService>();
        collection.AddSingleton<SqlExecutionErrorStore>();
        collection.AddSingleton<CodexAppServerClient>();
        collection.AddSingleton<ICopilotChatService, LocalChatService>();

        collection.AddEmbeddedLlamaServerServices();

        collection.AddTransient<ISqlCodeFormatterService, SqlCodeFormatterService>();
        collection.AddTransient<ISqlVariableProcessor, SqlVariableProcessor>();
        collection.AddTransient<ISqlFileWatcherService, SqlFileWatcherService>();
        collection.AddTransient<IDbObjectExplorerService, DbObjectExplorerService>();
        collection.AddTransient<ISqlImportService, SqlImportService>();
        collection.AddTransient<ISqlExportOperations, SqlExportOperations>();
        collection.AddTransient<ISqlConnectionManager, SqlConnectionManager>();
        collection.AddTransient<IDbObjectActionService, DbObjectActionService>();
        collection.AddTransient<ISqlExecutionService, SqlExecutionService>();
        collection.AddTransient<ISqlExecutionStateService, SqlExecutionStateService>();
        collection.AddTransient<ISqlResultDispatcherService, SqlResultDispatcherService>();
        collection.AddTransient<ISqlDocumentExecutionServices, SqlDocumentExecutionServices>();
        collection.AddTransient<ISqlDocumentInteractionServices, SqlDocumentInteractionServices>();
        collection.AddTransient<ISqlDocumentUiServices, SqlDocumentUiServices>();
        collection.AddSingleton(DatabaseServiceRegistry.UseSharedInstance());
        collection.AddSingleton<IDatabaseServiceResolver, DatabaseServiceResolver>();
        collection.AddTransient<IDatabaseListSyncService, DatabaseListSyncService>();
        collection.AddTransient<ISqlRunPreparationService, SqlRunPreparationService>();
        collection.AddTransient<ISqlRunLifecycleService, SqlRunLifecycleService>();
        collection.AddTransient<ISqlRunOrchestrationService, SqlRunOrchestrationService>();
        collection.AddSingleton<IDockLayoutBuilder, DockLayoutBuilder>();
        collection.AddSingleton<IDockSidePanelService, DockSidePanelService>();

        collection.AddSingleton<INotificationManagerProvider, NotificationManagerProvider>();

        collection.AddSingleton<ISummaryRowService, SummaryRowService>();
        collection.AddSingleton<IResultGridSummaryRefreshService, ResultGridSummaryRefreshService>();
        collection.AddSingleton<IResultGridSummaryScrollService, ResultGridSummaryScrollService>();
        collection.AddSingleton<IResultGridSelectionService, ResultGridSelectionService>();
        collection.AddSingleton<IResultGridDoubleTapService, ResultGridDoubleTapService>();
        collection.AddSingleton<IDataGridClipboardService, DataGridClipboardService>();
        collection.AddSingleton<IResultGridActionRoutingService, ResultGridActionRoutingService>();
        collection.AddSingleton<IResultGridGroupingService, ResultGridGroupingService>();
        collection.AddSingleton<IResultGridGroupingDragService, ResultGridGroupingDragService>();
        collection.AddSingleton<IResultGridGroupExpandCollapseService, ResultGridGroupExpandCollapseService>();

        collection.AddSingleton<IResultGridSearchService, ResultGridSearchService>();
        collection.AddSingleton<IResultGridStatsService, ResultGridStatsService>();
        collection.AddSingleton<IResultGridKeyboardService, ResultGridKeyboardService>();

        // SqlResultsView services aggregator for DI
        collection.AddSingleton<ISqlResultsViewServices, SqlResultsViewServices>();

    }
}
