using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using JustyBase.Common.Contracts;
using JustyBase.Public.Lib.Services;
using JustyBase.QuickOpen;
using JustyBase.Services;
using JustyBase.Services.Documents;
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

    [ObservableProperty]
    public partial IRootDock? Layout { get; set; }

    public bool? AutoDownloadUpdate => _generalApplicationData?.Config?.AutoDownloadUpdate;

    [ObservableProperty]
    public partial string CharAtMessage { get; set; }

    [ObservableProperty]
    public partial string SelectedRowsCount { get; set; }

    [RelayCommand]
    private async Task ShowAbout()
    {
        await _messageForUserTools.ShowAboutDialogAsync();
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

        _dockFactory?.ShowAiChat();
        RefreshViewPanelStates();
    }

    /// <summary>
    /// Feature availability flag for AI Chat (reads the live master switch). When false, the
    /// AI Chat dock panel is not created, "Fix in AI Chat" entry points are blocked, and the
    /// main-menu item is hidden.
    /// </summary>
    public bool IsAiChatEnabled => _generalApplicationData?.Config?.EnableAiChat ?? false;

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
    private async Task ShowQuickOpenAsync()
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
                });

            dialog = new QuickOpenWindow(vm);
            await dialog.ShowDialog(owner);

            if (accepted is null)
                return;

            // Dialog teardown restores focus to the previous control and can undo dock activation
            // (same reason AddNewDocumentFromFile posts activation at Input priority).
            QuickOpenHit hitToOpen = accepted;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    OpenQuickOpenHit(hitToOpen);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Quick Open open-hit failed: {ex}");
                    _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
                }
            }, DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quick Open failed: {ex}");
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
    }

    private void OpenQuickOpenHit(QuickOpenHit hit)
    {
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

        if (hit.Kind == QuickOpenHitKind.Content
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
        ISqlDocumentUiServices sqlDocumentUiServices)
    {
        _dockFactory = dockFactory;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _generalApplicationData = generalApplicationData;
        _mainWindowActivationService = mainWindowActivationService;
        _dockableCleanupService = dockableCleanupService;
        _messageForUserTools = messageForUserTools;
        _sqlDocumentUiServices = sqlDocumentUiServices;

        CharAtMessage = "";
        ConfigureDockFactoryBindings();

#if DEBUG
        DebugFactoryEvents(_dockFactory);
#endif

        RegisterFactoryEvents();
        InitializeLayout();
        RefreshViewPanelStates();
        OpenStartupSqlFile(Environment.GetCommandLineArgs());

        SetupPipeCommunication();
        _dockFactory.CloseOldAddNewConnection();
    }

    private void ConfigureDockFactoryBindings()
    {
        _dockFactory.AtCharAction = message => CharAtMessage = message;
        _dockFactory.SelectedDataGridAction = selectedRows => SelectedRowsCount = selectedRows;
    }

    private void RegisterFactoryEvents()
    {
        _dockFactory.ActiveDockableChanged += HandleActiveDockableChanged;
        _dockFactory.DockableClosed += Factory_DockableClosed;
    }

    private void HandleActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs args)
    {
        if (args.Dockable is not SqlDocumentViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsRecentlyFinished)
        {
            viewModel.IsRecentlyFinished = false;
        }

        _dockFactory.ActivateSqlDocument(viewModel);
    }

    private void Factory_DockableClosed(object sender, DockableClosedEventArgs e)
    {
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
    private void DebugFactoryEvents(DockFactory factory)
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

        factory.WindowClosing += (_, args) =>
        {
            // NOTE: Set to True to cancel window closing.
#if false
                args.Cancel = true;
#endif      
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
            // NOTE: Set to True to cancel window dragging.
#if false
                args.Cancel = true;
#endif
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
    //public void CloseLayout()
    //{
    //    if (Layout is IDock)
    //    {
    //        _dockFactory.SaveStartupSqlAndFiles();
    //    }
    //}
}
