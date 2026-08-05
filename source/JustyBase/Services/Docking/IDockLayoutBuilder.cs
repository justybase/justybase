using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.ViewModels.Docks;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using JustyBase.ViewModels.Views;
using Orientation = Dock.Model.Core.Orientation;

namespace JustyBase.Services.Docking;

public sealed record DockLayoutBuildResult(
    MainViewModel MainViewModel,
    IDocumentDock DocumentDock,
    ProportionalDock MiddleDock,
    SqlResultsFastViewModel ResultsViewModel);

public interface IDockLayoutBuilder
{
    DockLayoutBuildResult BuildLayout(IFactory dockFactory, IList<IDockable> documentsList);
}

public sealed class DockLayoutBuilder : IDockLayoutBuilder
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IDockViewModelFactory _viewModelFactory;
    private readonly IDockSqlDocumentFactory _dockSqlDocumentFactory;

    public DockLayoutBuilder(
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDockViewModelFactory viewModelFactory,
        IDockSqlDocumentFactory dockSqlDocumentFactory)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _viewModelFactory = viewModelFactory;
        _dockSqlDocumentFactory = dockSqlDocumentFactory;
    }

    public DockLayoutBuildResult BuildLayout(IFactory dockFactory, IList<IDockable> documentsList)
    {
        var toolSet = CreateToolSet(dockFactory);
        var resultsDock = CreateResultsDock(dockFactory, toolSet.ResultsViewModel, toolSet.DiagnosticsViewModel);
        var documentDock = CreateDocumentDock(documentsList);
        var middleDock = CreateMiddleDock(dockFactory, documentDock, resultsDock);
        var mainLayout = CreateStandardSideLayout(dockFactory, middleDock, toolSet);

        var mainViewModel = new MainViewModel
        {
            Id = "Home",
            Title = "Home",
            ActiveDockable = mainLayout,
            VisibleDockables = dockFactory.CreateList<IDockable>(mainLayout)
        };

        return new(mainViewModel, documentDock, middleDock, toolSet.ResultsViewModel);
    }

    private DockToolSet CreateToolSet(IFactory dockFactory)
    {
        var dbSchemaViewModel = _viewModelFactory.CreateDbSchemaViewModel();
        ConfigureDockable(dbSchemaViewModel, "DbSchema", "Schema");

        var outlineViewModel = _viewModelFactory.CreateSqlOutlineViewModel();
        ConfigureDockable(outlineViewModel, "SqlOutline", "Outline");

        var variablesViewModel = _viewModelFactory.CreateVariablesViewModel();
        ConfigureDockable(variablesViewModel, "Variables", "Variables");

        var logViewModel = _viewModelFactory.CreateLogToolViewModel();
        ConfigureDockable(logViewModel, "LogTool", "Log");

        var sessionMonitorViewModel = _viewModelFactory.CreateNetezzaSessionMonitorViewModel();
        ConfigureDockable(sessionMonitorViewModel, "NetezzaSessionMonitor", "NZ Sessions");

        var schemaSearchViewModel = new SchemaSearchViewModel(dockFactory, _generalApplicationData, _messageForUserTools, logViewModel)
        {
            Id = "schemaSearch",
            Title = "Schema search",
            CanClose = false,
            CanPin = true,
            CanFloat = false
        };
        DockCapabilityHelper.SyncOverridesFromFlags(schemaSearchViewModel);

        var fileExplorerViewModel = _viewModelFactory.CreateFileExplorerViewModel();
        ConfigureDockable(fileExplorerViewModel, "File explorer", "Files");

        var gitViewModel = _viewModelFactory.CreateGitViewModel();
        ConfigureDockable(gitViewModel, "Git", "Git");

        var resultsViewModel = _viewModelFactory.CreateSqlResultsFastViewModel();
        ConfigureDockable(resultsViewModel, "FastViewModel", "Results");

        var diagnosticsViewModel = _viewModelFactory.CreateSqlDiagnosticsViewModel();
        ConfigureDockable(diagnosticsViewModel, "SqlDiagnostics", "Diagnostics");

        AiChatViewModel? aiChatViewModel = null;
        if (_generalApplicationData.Config.EnableAiChat)
        {
            aiChatViewModel = _viewModelFactory.CreateAiChatViewModel();
            ConfigureDockable(aiChatViewModel, "AiChat", "AI Chat");
        }

        return new(
            dbSchemaViewModel,
            variablesViewModel,
            schemaSearchViewModel,
            fileExplorerViewModel,
            gitViewModel,
            logViewModel,
            sessionMonitorViewModel,
            resultsViewModel,
            diagnosticsViewModel,
            outlineViewModel,
            aiChatViewModel);
    }

    private CustomDocumentDock CreateDocumentDock(IList<IDockable> documentsList)
    {
        foreach (var document in documentsList)
        {
            if (document is SqlDocumentViewModel sqlDocument)
            {
                sqlDocument.CanFloat = false;
                sqlDocument.CanDrag = true;
                sqlDocument.AllowedDockOperations = DockOperationMask.Fill;
            }

            DockCapabilityHelper.SyncOverridesFromFlags(document);
        }

        var documentDock = new CustomDocumentDock(_dockSqlDocumentFactory)
        {
            IsCollapsable = false,
            ActiveDockable = documentsList[0],
            VisibleDockables = documentsList,
            CanCreateDocument = true,
            TabsLayout = DocumentTabLayout.Top,
            CanPin = true,
            CanDrop = true,
            AllowedDropOperations = DockOperationMask.Fill
        };
        DockCapabilityHelper.SyncOverridesFromFlags(documentDock);
        return documentDock;
    }

    private static ToolDock CreateResultsDock(
        IFactory dockFactory,
        SqlResultsFastViewModel resultsViewModel,
        SqlDiagnosticsViewModel diagnosticsViewModel)
    {
        DockCapabilityHelper.SyncOverridesFromFlags(resultsViewModel);
        DockCapabilityHelper.SyncOverridesFromFlags(diagnosticsViewModel);

        var resultsDock = new ToolDock
        {
            Title = "ResultsDock",
            ActiveDockable = resultsViewModel,
            VisibleDockables = dockFactory.CreateList<IDockable>(resultsViewModel, diagnosticsViewModel),
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Hidden,
            CanClose = false,
            AutoHide = false,
            IsCollapsable = false,
            CanPin = false,
            CanFloat = false,
            Proportion = 0.25
        };
        DockCapabilityHelper.SyncOverridesFromFlags(resultsDock);
        return resultsDock;
    }

    private static ProportionalDock CreateMiddleDock(IFactory dockFactory, IDocumentDock documentDock, ToolDock resultsDock)
    {
        return new ProportionalDock
        {
            Proportion = 0.75,
            Title = "MiddleDock",
            Orientation = Orientation.Vertical,
            ActiveDockable = null,
            VisibleDockables = dockFactory.CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter(),
                resultsDock)
        };
    }

    private ProportionalDock CreateStandardSideLayout(IFactory dockFactory, ProportionalDock middleDock, DockToolSet toolSet)
    {
        var sideDock = CreateSideDock(dockFactory, toolSet);

        var layoutDockables = new List<IDockable>
        {
            sideDock,
            new ProportionalDockSplitter(),
            middleDock
        };

        if (toolSet.AiChatViewModel is not null)
        {
            layoutDockables.Add(new ProportionalDockSplitter());
            layoutDockables.Add(CreateAiChatDock(dockFactory, toolSet.AiChatViewModel));
        }

        return new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = dockFactory.CreateList<IDockable>(layoutDockables.ToArray())
        };
    }

    private ProportionalDock CreateSideDock(IFactory dockFactory, DockToolSet toolSet)
    {
        return new ProportionalDock
        {
            Proportion = 0.25,
            Orientation = Orientation.Vertical,
            ActiveDockable = null,
            VisibleDockables = dockFactory.CreateList<IDockable>(
                CreateToolDock(dockFactory, toolSet.DbSchemaViewModel, Alignment.Left, toolSet.OutlineViewModel, toolSet.VariablesViewModel),
                new ProportionalDockSplitter(),
                CreateToolDock(
                    dockFactory,
                    toolSet.SchemaSearchViewModel,
                    Alignment.Left,
                    toolSet.FileExplorerViewModel,
                    toolSet.GitViewModel,
                    toolSet.LogViewModel,
                    toolSet.SessionMonitorViewModel))
        };
    }

    private static ToolDock CreateAiChatDock(IFactory dockFactory, AiChatViewModel aiChatViewModel)
    {
        var rightDock = new ToolDock
        {
            Id = "AiChatDock",
            Title = "AI Chat",
            Proportion = 0.25,
            ActiveDockable = aiChatViewModel,
            VisibleDockables = dockFactory.CreateList<IDockable>(aiChatViewModel),
            Alignment = Alignment.Right
        };
        DockCapabilityHelper.SyncOverridesFromFlags(rightDock);
        return rightDock;
    }

    private static ToolDock CreateToolDock(
        IFactory dockFactory,
        IDockable activeDockable,
        Alignment alignment,
        params IDockable[] additionalDockables)
    {
        var dockables = new IDockable[1 + additionalDockables.Length];
        dockables[0] = activeDockable;
        additionalDockables.CopyTo(dockables, 1);

        var toolDock = new ToolDock
        {
            ActiveDockable = activeDockable,
            VisibleDockables = dockFactory.CreateList(dockables),
            Alignment = alignment
        };
        DockCapabilityHelper.SyncOverridesFromFlags(toolDock);
        return toolDock;
    }

    private static void ConfigureDockable(IDockable dockable, string id, string title)
    {
        dockable.Id = id;
        dockable.Title = title;
        dockable.CanClose = false;
        dockable.CanPin = true;
        dockable.CanFloat = false;
        DockCapabilityHelper.SyncOverridesFromFlags(dockable);
    }

    private sealed record DockToolSet(
        DbSchemaViewModel DbSchemaViewModel,
        VariablesViewModel VariablesViewModel,
        SchemaSearchViewModel SchemaSearchViewModel,
        FileExplorerViewModel FileExplorerViewModel,
        GitViewModel GitViewModel,
        LogToolViewModel LogViewModel,
        NetezzaSessionMonitorViewModel SessionMonitorViewModel,
        SqlResultsFastViewModel ResultsViewModel,
        SqlDiagnosticsViewModel DiagnosticsViewModel,
        SqlOutlineViewModel OutlineViewModel,
        AiChatViewModel? AiChatViewModel);
}
