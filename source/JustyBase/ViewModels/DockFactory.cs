using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Common.Helpers;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.Docking;
using JustyBase.ViewModels.Docks;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using JustyBase.Helpers;
using JustyBase.ViewModels.Views;
using System.Data.Common;
using System.Diagnostics;

namespace JustyBase.ViewModels;

public sealed class DockFactory(IGeneralApplicationData generalApplicationData, IOtherHelpers otherHelpers, ISimpleLogger simpleLogger, IEncryptionHelper encryptionHelper,
    IMessageForUserTools messageForUserTools, IDockViewModelFactory viewModelFactory, IDockLayoutBuilder dockLayoutBuilder, IDockSidePanelService dockSidePanelService,
    IDockDocumentActivationService dockDocumentActivationService, IDockSessionPersistenceService dockSessionPersistenceService,
    IDockSqlDocumentFactory dockSqlDocumentFactory, IDockResultRoutingService dockResultRoutingService,
    IDockFileOpenService dockFileOpenService) : Factory, ISqlResultManager, IActiveDocumentManager, IGitDiffPresentationService
{
    private readonly IGeneralApplicationData _generalApplicationData = generalApplicationData;
    private readonly ISimpleLogger _simpleLogger = simpleLogger;
    private readonly IOtherHelpers _otherHelpers = otherHelpers;
    private readonly IEncryptionHelper _encryptionHelper = encryptionHelper;
    private readonly IMessageForUserTools _messageForUserTools = messageForUserTools;
    private readonly IDockViewModelFactory _viewModelFactory = viewModelFactory;
    private readonly IDockLayoutBuilder _dockLayoutBuilder = dockLayoutBuilder;
    private readonly IDockSidePanelService _dockSidePanelService = dockSidePanelService;
    private readonly IDockDocumentActivationService _dockDocumentActivationService = dockDocumentActivationService;
    private readonly IDockSessionPersistenceService _dockSessionPersistenceService = dockSessionPersistenceService;
    private readonly IDockSqlDocumentFactory _dockSqlDocumentFactory = dockSqlDocumentFactory;
    private readonly IDockResultRoutingService _dockResultRoutingService = dockResultRoutingService;
    private readonly IDockFileOpenService _dockFileOpenService = dockFileOpenService;
    private IRootDock? _rootDock;

    private IDocumentDock? _mainDocumentDockTmp;
    private IDocumentDock? MainDocumentDock
    {
        get
        {
            if (_mainDocumentDockTmp is not null)
            {
                return _mainDocumentDockTmp;
            }

            _mainDocumentDockTmp = FindDockable(_rootDock, a => a is DocumentDock) as IDocumentDock;
            return _mainDocumentDockTmp;
        }
    }

    public bool IsLastDocument()
    {
        return MainDocumentDock?.VisibleDockables?.Count == 1;
    }

    public void ResetMainDocumentDockTmp()
    {
        _mainDocumentDockTmp = null;
    }

    public override IDocumentDock CreateDocumentDock()
    {
        // Fallback host used by Dock when it needs to materialize a document dock. SQL source
        // and destination operations are restricted to Fill, so it remains a tab host only.
        var dock = new CustomDocumentDock(_dockSqlDocumentFactory)
        {
            Title = "Documents",
            IsCollapsable = false,
            CanCreateDocument = true,
            TabsLayout = DocumentTabLayout.Top,
            CanPin = true,
            CanFloat = false,
            CanClose = false,
            CanDrop = true,
            AllowedDropOperations = DockOperationMask.Fill
        };
        DockCapabilityHelper.SyncOverridesFromFlags(dock);
        return dock;
    }

    public override void AddDockable(IDock dock, IDockable dockable)
    {
        DockCapabilityHelper.SyncOverridesFromFlags(dockable);
        base.AddDockable(dock, dockable);
    }

    public override void InsertDockable(IDock dock, IDockable dockable, int index)
    {
        DockCapabilityHelper.SyncOverridesFromFlags(dockable);
        base.InsertDockable(dock, dockable, index);
    }

    public override IRootDock CreateLayout()
    {
        _rootDock = CreateFreshLayout();
        return _rootDock;
    }

    public void CloseOldAddNewConnection()
    {
        try
        {
            var addNewOldTool = this.FindDockable(_rootDock, x => x.Id == "newConnectionTab");
            if (addNewOldTool is not null)
            {
                addNewOldTool.CanClose = true;
                (addNewOldTool.Owner as ToolDock)?.VisibleDockables.Remove(addNewOldTool);
            }
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
    }

    private SqlResultsFastViewModel _sqlResultsFastViewModel;
    public SqlResultsFastViewModel SqlResultsFastViewModel => _sqlResultsFastViewModel;

    public IRootDock CreateFreshLayout()
    {
        IList<IDockable> documentsList = CreateList<IDockable>();
        foreach (var (tabId, offlineTabData) in _generalApplicationData.GetDocumentsKeyValueCollection())
        {
            if (offlineTabData.HotDocumentViewModel is not null)
            {
                var doc = offlineTabData.HotDocumentViewModelAsT<SqlDocumentViewModel>();
                doc.SelectedConnectionIndex = offlineTabData.ConnectionIndex;
                documentsList.Add(doc);
                ActiveSqlDocumentViewModel = doc;
            }
            else
            {
                var doc = _viewModelFactory.CreateSqlDocumentViewModel();
                doc.Id = tabId;
                doc.Title = offlineTabData.Title;
                doc.FontSize = offlineTabData.FontSize;

                doc.Id = tabId;
                doc.SelectedConnectionIndex = offlineTabData.ConnectionIndex;
                if (offlineTabData.SqlFilePath is not null)
                {
                    doc.FilePath = offlineTabData.SqlFilePath;
                }

                offlineTabData.HotDocumentViewModel = doc;
                documentsList.Add(doc);
                ActiveSqlDocumentViewModel = doc;
            }
        }
        if (documentsList.Count == 0)
        {
            string title = "your first sql";
            SqlDocumentViewModel newDockable = _dockSqlDocumentFactory.CreateDocument(
                title,
                fontSize: _generalApplicationData.Config.DefaultFontSizeForDocuments);
            documentsList.Add(newDockable);
            ActiveSqlDocumentViewModel = newDockable;
        }

        ActiveSqlDocumentViewModel ??= documentsList.OfType<SqlDocumentViewModel>().FirstOrDefault();

        DockLayoutBuildResult layoutResult = _dockLayoutBuilder.BuildLayout(this, documentsList);
        _sqlResultsFastViewModel = layoutResult.ResultsViewModel;
        _mainDocumentDockTmp = layoutResult.DocumentDock;
        _middleDock = layoutResult.MiddleDock;
        _hiddenToolPanels.Clear();


        var rootDock = CreateRootDock();
        rootDock.IsCollapsable = false;
        //rootDock.Id = "Root";
        //rootDock.Title = "Root";
        rootDock.ActiveDockable = layoutResult.MainViewModel;
        rootDock.DefaultDockable = layoutResult.MainViewModel;
        rootDock.VisibleDockables = CreateList<IDockable>(layoutResult.MainViewModel);
        _rootDock = rootDock;

        return rootDock;
    }

    private readonly List<IDockable> _hidenDockables = [];
    private readonly Dictionary<string, HiddenToolPanel> _hiddenToolPanels = new(StringComparer.Ordinal);
    private ProportionalDock? _middleDock;

    public static IReadOnlyList<(string Id, string Title)> ToggleableToolPanels { get; } =
    [
        ("DbSchema", "Schema"),
        ("SqlOutline", "Outline"),
        ("Variables", "Variables"),
        ("schemaSearch", "Schema search"),
        ("File explorer", "Files"),
        ("Git", "Git"),
        ("LogTool", "Log"),
        ("NetezzaSessionMonitor", "NZ Sessions"),
        ("FastViewModel", "Results"),
        ("SqlDiagnostics", "Diagnostics"),
        ("AiChat", "AI Chat")
    ];

    public void HideOrShowSideElements()
    {
        _middleDock = _dockSidePanelService.ResolveMiddleDock(_rootDock, _middleDock);
        if (_middleDock is null)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("middleDock is null");
            return;
        }

        if (_rootDock?.ActiveDockable is not MainViewModel { ActiveDockable: ProportionalDock layoutDock })
        {
            return;
        }

        if (_hidenDockables.Count > 0)
        {
            _dockSidePanelService.RestoreSideElements(layoutDock, _middleDock, _hidenDockables);
            return;
        }

        _dockSidePanelService.HideSideElements(layoutDock, _middleDock, _hidenDockables);
    }

    public bool AreSidePanelsVisible => _hidenDockables.Count == 0;

    public bool IsToolPanelVisible(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return false;
        }

        if (_hiddenToolPanels.ContainsKey(toolId))
        {
            return false;
        }

        if (toolId == "AiChat")
        {
            return FindDockable(_rootDock, x => x.Id == "AiChat") is not null
                && _rootDock?.ActiveDockable is MainViewModel { ActiveDockable: ProportionalDock layoutDock }
                && layoutDock.VisibleDockables?.Any(DockSidePanelService.IsAiChatDock) == true;
        }

        return FindDockable(_rootDock, x => x.Id == toolId) is not null;
    }

    public void ToggleToolPanel(string toolId)
    {
        if (IsToolPanelVisible(toolId))
        {
            HideToolPanel(toolId);
        }
        else
        {
            ShowToolPanel(toolId);
        }
    }

    public void ShowToolPanel(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return;
        }

        if (_hidenDockables.Count > 0 && toolId != "AiChat")
        {
            HideOrShowSideElements();
        }

        if (_hiddenToolPanels.TryGetValue(toolId, out var hidden))
        {
            _hiddenToolPanels.Remove(toolId);
            if (toolId == "AiChat")
            {
                EnsureAiChatDockedOnRight(hidden.Tool as AiChatViewModel);
                return;
            }

            if (hidden.Owner.VisibleDockables is null)
            {
                return;
            }

            int index = Math.Clamp(hidden.Index, 0, hidden.Owner.VisibleDockables.Count);
            InsertDockable(hidden.Owner, hidden.Tool, index);
            SetActiveDockable(hidden.Tool);
            SetFocusedDockable(hidden.Owner, hidden.Tool);
            return;
        }

        if (toolId == "AiChat")
        {
            ShowAiChat();
            return;
        }

        var tool = FindDockable(_rootDock, x => x.Id == toolId);
        if (tool is null)
        {
            return;
        }

        SetActiveDockable(tool);
        if (tool.Owner is IDock owner)
        {
            SetFocusedDockable(owner, tool);
        }
    }

    public void HideToolPanel(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return;
        }

        if (toolId == "AiChat")
        {
            HideAiChatPanel();
            return;
        }

        var tool = FindDockable(_rootDock, x => x.Id == toolId);
        if (tool?.Owner is not IDock owner || owner.VisibleDockables is null)
        {
            return;
        }

        int index = owner.VisibleDockables.IndexOf(tool);
        if (index < 0)
        {
            return;
        }

        _hiddenToolPanels[toolId] = new HiddenToolPanel(tool, owner, index);
        RemoveDockable(tool, collapse: false);
    }

    private void HideAiChatPanel()
    {
        if (_rootDock?.ActiveDockable is not MainViewModel { ActiveDockable: ProportionalDock layoutDock }
            || layoutDock.VisibleDockables is null)
        {
            return;
        }

        var aiChat = FindDockable(_rootDock, x => x.Id == "AiChat");
        if (aiChat?.Owner is IDock owner)
        {
            _hiddenToolPanels["AiChat"] = new HiddenToolPanel(aiChat, owner, 0);
        }

        foreach (var existing in layoutDock.VisibleDockables.Where(DockSidePanelService.IsAiChatDock).ToList())
        {
            layoutDock.VisibleDockables.Remove(existing);
        }

        while (layoutDock.VisibleDockables.Count > 0
               && layoutDock.VisibleDockables[^1] is IProportionalDockSplitter)
        {
            layoutDock.VisibleDockables.RemoveAt(layoutDock.VisibleDockables.Count - 1);
        }
    }

    private sealed record HiddenToolPanel(IDockable Tool, IDock Owner, int Index);
    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object>>
        {
            ["DbSchema"] = () => new object(),
            ["Variables"] = () => new object(),
            ["SchemaSearch"] = () => new object(),
            ["FileExplorer"] = () => new object(),
            ["Git"] = () => new object(),
            ["LogTool"] = () => new object(),
            ["Dashboard"] = () => layout,
            ["Home"] = () => () => new object()
        };

        //foreach (var item in _generalApplicationData.GetDocumentsKeyValueCollection())
        //{
        //    ContextLocator[item.Key] = (() => new object());
        //}

        DockableLocator = new Dictionary<string, Func<IDockable?>>()
        {
            ["Root"] = () => _rootDock,
            ["Documents"] = () => MainDocumentDock,
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
        if (_generalApplicationData.TryGetDocumentById(_generalApplicationData.SelectedTabIdFromStart, out var savedTabData) && savedTabData.HotDocumentViewModel is IDockable dockable)
        {
            MainDocumentDock.ActiveDockable = dockable;
        }

    }

    public void ClosePrevResults(string id)
    {
        _sqlResultsFastViewModel.ClearFromDocument(id, false);
    }

    public void AddNewResult((IDatabaseService? dbService, DbDataReader? rdr, string errorMessage) res, string id, int queryNum, ref int abortUbound, string? sql, DbCommand? command, string? title)
    {
        if (!_generalApplicationData.TryGetDocumentById(id, out var result)
            || result.HotDocumentViewModelAsT<SqlDocumentViewModel>() is not SqlDocumentViewModel document)
        {
            return;
        }

        _dockResultRoutingService.AddResult(
            res,
            id,
            document,
            queryNum,
            ref abortUbound,
            sql,
            command,
            title,
            _sqlResultsFastViewModel,
            IsActiveDockable(document));
    }

    public void ResultsFromActiveTab(SqlDocumentViewModel viewModel)
    {
        Debug.Assert(_sqlResultsFastViewModel is not null);
        ActiveSqlDocumentViewModel = viewModel;
        _dockResultRoutingService.SyncActiveDocumentResults(
            viewModel,
            _sqlResultsFastViewModel,
            id => _viewModelFactory.CreateLogToolViewModel().SwitchLogs(id));
    }

    public void ActivateSqlDocument(SqlDocumentViewModel viewModel)
    {
        ActiveSqlDocumentViewModel = viewModel;
        ResultsFromActiveTab(viewModel);
        viewModel.OnActivated();
        NotifyGitActiveDocumentChanged();
    }

    private void NotifyGitActiveDocumentChanged()
    {
        foreach (GitViewModel git in Find(d => d is GitViewModel).OfType<GitViewModel>())
            git.SyncActiveDocumentFile();
    }

    public void ActivateCurrentSqlDocument()
    {
        if (MainDocumentDock?.ActiveDockable is SqlDocumentViewModel viewModel)
        {
            ActivateSqlDocument(viewModel);
        }
    }

    public List<SqlResultsViewModel> GetDocumentResults(SqlDocumentViewModel viewModel)
    {
        List<SqlResultsViewModel> results = [];
        var collection = _sqlResultsFastViewModel.GetDocumentResults(viewModel);
        foreach (var result in collection)
        {
            results.Add(result);
        }
        return results;
    }

    public void SaveStartupSqlAndFiles(string? selectedTabId = null)
    {
        if (string.IsNullOrWhiteSpace(selectedTabId))
        {
            selectedTabId = MainDocumentDock.ActiveDockable?.Id;
        }

        if (string.IsNullOrWhiteSpace(selectedTabId) && MainDocumentDock.VisibleDockables is not null)
        {
            selectedTabId = MainDocumentDock.VisibleDockables
                .Select(d => d.Id)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        }

        OfflineDocumentContainer offlineDocumentContainer =
            _generalApplicationData.GetOfflineDocumentContainer(selectedTabId ?? string.Empty);
        _dockSessionPersistenceService.SaveSession(
            new DockSessionSaveRequest(
                SelectedTabId: selectedTabId,
                OfflineDocumentContainer: offlineDocumentContainer,
                VisibleDockables: MainDocumentDock.VisibleDockables,
                SaveEncodedText: content => _encryptionHelper.SaveTextFileEncoded(IGeneralApplicationData.StartupPath, content)));
    }

    public void MakeAllResultsHidden()
    {
        _sqlResultsFastViewModel.HideAllResult();
    }

    public T? GetViewModelOfType<T>() where T : class, IDockable
    {
        return _dockDocumentActivationService.GetDocumentOfType<T>(MainDocumentDock.VisibleDockables);
    }

    public void AddHistoryDocument()
    {
        HistoryViewModel historyViewModel = _dockDocumentActivationService.EnsureDocument(
            MainDocumentDock.VisibleDockables,
            _viewModelFactory.CreateHistoryViewModel,
            recreateExisting: true);
        MainDocumentDock.ActiveDockable = historyViewModel;
    }

    public void AddSettingsDocument()
    {
        SettingsViewModel settingsViewModel = _dockDocumentActivationService.EnsureDocument(
            MainDocumentDock.VisibleDockables,
            _viewModelFactory.CreateSettingsViewModel);
        MainDocumentDock.ActiveDockable = settingsViewModel;
    }
    public void AddImportDocument()
        => OpenImportDocument();

    public void ShowAiChat()
    {
        if (_hidenDockables.Count > 0)
        {
            HideOrShowSideElements();
        }

        _hiddenToolPanels.Remove("AiChat");

        var aiChat = FindDockable(_rootDock, x => x.Id == "AiChat") as AiChatViewModel;
        if (aiChat is null || !IsAiChatOnRightEdge(aiChat))
        {
            aiChat = EnsureAiChatDockedOnRight(aiChat);
            if (aiChat is null)
            {
                return;
            }
        }

        if (aiChat.OriginalOwner is not null)
        {
            UnpinDockable(aiChat);
        }

        SetActiveDockable(aiChat);
        if (aiChat.Owner is IDock owner)
        {
            SetFocusedDockable(owner, aiChat);
        }
    }

    private bool IsAiChatOnRightEdge(AiChatViewModel aiChat)
    {
        if (_rootDock?.ActiveDockable is not MainViewModel { ActiveDockable: ProportionalDock layoutDock }
            || layoutDock.VisibleDockables is null
            || layoutDock.VisibleDockables.Count == 0)
        {
            return false;
        }

        var rightmost = layoutDock.VisibleDockables[^1];
        return DockSidePanelService.IsAiChatDock(rightmost)
            && (ReferenceEquals(aiChat.Owner, rightmost)
                || (rightmost is IDock dock && dock.VisibleDockables?.Contains(aiChat) == true));
    }

    private AiChatViewModel? EnsureAiChatDockedOnRight(AiChatViewModel? existing = null)
    {
        if (_rootDock?.ActiveDockable is not MainViewModel { ActiveDockable: ProportionalDock layoutDock }
            || layoutDock.VisibleDockables is null)
        {
            return null;
        }

        var aiChat = existing ?? _viewModelFactory.CreateAiChatViewModel();
        aiChat.Id = "AiChat";
        aiChat.Title = "AI Chat";
        aiChat.CanClose = false;
        aiChat.CanPin = true;
        aiChat.CanFloat = false;
        DockCapabilityHelper.SyncOverridesFromFlags(aiChat);

        // Remove from previous host (tab strip / old dock) if present.
        if (aiChat.Owner is IDock)
        {
            RemoveDockable(aiChat, collapse: true);
        }

        // Drop any existing right AI host before re-adding at the edge.
        foreach (var existingDock in layoutDock.VisibleDockables.Where(DockSidePanelService.IsAiChatDock).ToList())
        {
            layoutDock.VisibleDockables.Remove(existingDock);
        }

        while (layoutDock.VisibleDockables.Count > 0
               && layoutDock.VisibleDockables[^1] is IProportionalDockSplitter)
        {
            layoutDock.VisibleDockables.RemoveAt(layoutDock.VisibleDockables.Count - 1);
        }

        var rightDock = new ToolDock
        {
            Id = "AiChatDock",
            Title = "AI Chat",
            Proportion = 0.25,
            ActiveDockable = aiChat,
            VisibleDockables = CreateList<IDockable>(aiChat),
            Alignment = Alignment.Right
        };
        DockCapabilityHelper.SyncOverridesFromFlags(rightDock);

        layoutDock.VisibleDockables.Add(new ProportionalDockSplitter());
        AddDockable(layoutDock, rightDock);
        return aiChat;
    }

    public void OpenImportDocument(string? connectionName = null, string? database = null, string? schema = null, string? table = null)
    {
        ImportViewModel importViewModel = _dockDocumentActivationService.EnsureDocument(
            MainDocumentDock.VisibleDockables,
            _viewModelFactory.CreateImportViewModel);
        MainDocumentDock.ActiveDockable = importViewModel;

        if (!string.IsNullOrWhiteSpace(connectionName)
            || !string.IsNullOrWhiteSpace(database)
            || !string.IsNullOrWhiteSpace(schema)
            || !string.IsNullOrWhiteSpace(table))
        {
            importViewModel.ApplyImportContext(connectionName, database, schema, table);
        }
    }

    public Task StartQuickImportAsync(string sourcePath, string? connectionName, string? database)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return RunQuickImportAsync(sourcePath, connectionName, database);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RunQuickImportAsync(sourcePath, connectionName, database);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private async Task RunQuickImportAsync(string sourcePath, string? connectionName, string? database)
    {
        ImportViewModel importViewModel = _dockDocumentActivationService.EnsureDocument(
            MainDocumentDock.VisibleDockables,
            _viewModelFactory.CreateImportViewModel);
        MainDocumentDock.ActiveDockable = importViewModel;
        await importViewModel.StartQuickImportAsync(sourcePath, connectionName, database);
    }
    public void AddEtlDocument()
    {
        EtlViewModel etlViewModel = _dockDocumentActivationService.EnsureDocument(
            MainDocumentDock.VisibleDockables,
            _viewModelFactory.CreateEtlViewModel);
        MainDocumentDock.ActiveDockable = etlViewModel;
    }

    public void ShowGitDiff(string title, string oldText, string newText)
    {
        void Apply()
        {
            GitDiffDocumentViewModel diffVm = _dockDocumentActivationService.EnsureDocument(
                MainDocumentDock.VisibleDockables,
                _viewModelFactory.CreateGitDiffDocumentViewModel);
            diffVm.SetContents(title, oldText, newText);
            MainDocumentDock.ActiveDockable = diffVm;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public SqlDocumentViewModel? ActiveSqlDocumentViewModel { get; set; }
    private SqlDocumentViewModel? ResolveActiveSqlDocumentViewModel()
    {
        if (MainDocumentDock?.ActiveDockable is SqlDocumentViewModel activeSqlDocument)
        {
            ActiveSqlDocumentViewModel = activeSqlDocument;
        }

        return ActiveSqlDocumentViewModel;
    }

    public void InsertTextToActiveDocument(object data, bool rawMode)
    {
        ResolveActiveSqlDocumentViewModel()?.InsertTextRequest(data, rawMode);
    }

    public void InsertSnippetTextToActiveDocument(string text, string connectionName)
    {
        var activeSqlDocument = ResolveActiveSqlDocumentViewModel();
        activeSqlDocument?.InserSnippet(text);
        activeSqlDocument?.TrySetConnection(connectionName);
        try
        {
            activeSqlDocument?.SqlEditor?.ForceUpdateFoldings();
            activeSqlDocument?.SqlEditor?.CollapseFoldings();
        }
        catch (Exception ex)
        {
            _simpleLogger.LogAndShowError(ex, _messageForUserTools);
        }
    }

    public Action<string>? AtCharAction { get; set; }
    public Action<string>? SelectedDataGridAction { get; set; }

    public void AddNewDocumentFromFile(IEnumerable<string> files)
    {
        if (MainDocumentDock?.VisibleDockables is null)
        {
            return;
        }

        // Place new tabs directly after the tab that triggered open (active document).
        int insertIndex = MainDocumentDock.VisibleDockables.Count;
        if (MainDocumentDock.ActiveDockable is not null)
        {
            int activeIndex = MainDocumentDock.VisibleDockables.IndexOf(MainDocumentDock.ActiveDockable);
            if (activeIndex >= 0)
            {
                insertIndex = activeIndex + 1;
            }
        }

        SqlDocumentViewModel? openedDocument = _dockFileOpenService.PrepareDocuments(
            files,
            MainDocumentDock.VisibleDockables,
            out IReadOnlyList<SqlDocumentViewModel> documentsToDock);

        // InsertDockable runs InitDockable (sets Owner/Factory). Without that, SetActiveDockable is a no-op.
        foreach (SqlDocumentViewModel document in documentsToDock)
        {
            InsertDockable(MainDocumentDock, document, insertIndex);
            insertIndex++;
        }

        if (openedDocument is not null)
        {
            // File picker close can restore focus after this call returns — activate on next input pass.
            SqlDocumentViewModel toActivate = openedDocument;
            Dispatcher.UIThread.Post(() => ActivateOpenedSqlDocument(toActivate), DispatcherPriority.Input);
        }
    }

    public SqlDocumentViewModel? FindOpenSqlDocument(string? documentId, string? filePath)
    {
        IEnumerable<SqlDocumentViewModel> docs =
            MainDocumentDock?.VisibleDockables?.OfType<SqlDocumentViewModel>() ?? [];

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            var byId = docs.FirstOrDefault(d => string.Equals(d.Id, documentId, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            string normalized = NormalizeFilePath(filePath);
            return docs.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath)
                && string.Equals(NormalizeFilePath(d.FilePath!), normalized, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public void FocusSqlDocument(SqlDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ActivateOpenedSqlDocument(document);
    }

    private static string NormalizeFilePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public IReadOnlyList<SqlDocumentViewModel> GetOpenSqlDocuments()
        => MainDocumentDock?.VisibleDockables?.OfType<SqlDocumentViewModel>().ToArray()
           ?? [];

    private void ActivateOpenedSqlDocument(SqlDocumentViewModel document)
    {
        if (MainDocumentDock is null)
        {
            return;
        }

        MainDocumentDock.ActiveDockable = document;
        SetActiveDockable(document);
        SetFocusedDockable(MainDocumentDock, document);
        ActiveSqlDocumentViewModel = document;
        ActivateSqlDocument(document);
    }

    public bool IsActiveDockable(IDockable dockable)
    {
        return MainDocumentDock?.ActiveDockable?.Id == dockable?.Id;
    }

    public SqlDocumentViewModel AddNewDocumentFromTxtPreview(string path)
    {
        var res = _otherHelpers.CsvTxtPreviewer(path);
        return AddNewDocument(res, true);
    }

    public SqlDocumentViewModel AddNewDocument(string? initText = null, bool txtPreview = false, string? forcedTitle = null)
    {
        string title = forcedTitle ?? "Document" + (MainDocumentDock.VisibleDockables.Count + 1);
        SqlDocumentViewModel newDockable = _dockSqlDocumentFactory.CreateDocument(
            title,
            initText,
            txtPreview,
            fontSize: _generalApplicationData.Config.DefaultFontSizeForDocuments);
        MainDocumentDock.VisibleDockables.Add(newDockable);
        MainDocumentDock.ActiveDockable = newDockable;
        ActiveSqlDocumentViewModel = newDockable;
        return newDockable;
    }

    public void NextActiveDocument(object data)
    {
        int cnt = MainDocumentDock.VisibleDockables.Count;
        int activeIndex = MainDocumentDock.VisibleDockables.IndexOf(MainDocumentDock.ActiveDockable);
        int sign = 1;
        if (data.ToString() == "-")
        {
            sign = -1;
        }

        MainDocumentDock.ActiveDockable = MainDocumentDock.VisibleDockables[(cnt + activeIndex + sign) % cnt];
    }
}
