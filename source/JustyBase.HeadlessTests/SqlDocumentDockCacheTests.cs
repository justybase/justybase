using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Editor;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using JustyBase.Views.Documents;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Verifies SQL tab content lifetime: typed template creates distinct views, and a Dock-style
/// document content cache (CacheDocumentTabContent) keeps editor visual state across switches.
/// </summary>
public sealed class SqlDocumentDockCacheTests : HeadlessSessionTestBase
{
    [Fact]
    public Task DocumentContentCache_SwitchA_ThenB_ThenA_KeepsSameViewAndEditorState() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);

        var vmA = CreateSqlDocumentViewModel("doc-a", "SELECT 'A';" + Environment.NewLine + string.Concat(Enumerable.Repeat("-- line A" + Environment.NewLine, 80)));
        var vmB = CreateSqlDocumentViewModel("doc-b", "SELECT 'B';" + Environment.NewLine + string.Concat(Enumerable.Repeat("-- line B" + Environment.NewLine, 80)));

        // Mirrors DockFluentTheme CacheDocumentTabContent: one live view per open document.
        var cache = new Dictionary<object, Control>();
        var host = new ContentControl { Width = 640, Height = 480 };

        Control Activate(SqlDocumentViewModel vm)
        {
            if (!cache.TryGetValue(vm, out var view))
            {
                view = template.Build(vm) ?? throw new InvalidOperationException("template returned null");
                view.DataContext = vm;
                cache[vm] = view;
            }

            host.Content = view;
            return view;
        }

        var window = new Window
        {
            Width = 700,
            Height = 520,
            Content = host,
            Title = "SQL document content cache"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewA1 = Assert.IsType<SqlDocumentView>(Activate(vmA));
        Dispatcher.UIThread.RunJobs();
        var editorA = viewA1.FindControl<SqlCodeEditor>("SqlEditor");
        Assert.NotNull(editorA);
        Assert.Same(editorA, vmA.SqlEditor);

        editorA.Select(8, 4);
        editorA.CaretOffset = 8;
        var expectedSelectionStart = editorA.SelectionStart;
        var expectedSelectionLength = editorA.SelectionLength;
        var expectedCaret = editorA.CaretOffset;
        var scrollA = editorA.TextArea.TextView.ScrollOffset;
        Dispatcher.UIThread.RunJobs();

        var viewB = Assert.IsType<SqlDocumentView>(Activate(vmB));
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(viewA1, viewB);
        var editorB = viewB.FindControl<SqlCodeEditor>("SqlEditor");
        Assert.NotNull(editorB);
        editorB.Select(0, 2);
        editorB.CaretOffset = 0;
        var expectedBStart = editorB.SelectionStart;
        var expectedBLength = editorB.SelectionLength;
        Dispatcher.UIThread.RunJobs();

        var viewA2 = Assert.IsType<SqlDocumentView>(Activate(vmA));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(viewA1, viewA2);
        Assert.Same(editorA, viewA2.FindControl<SqlCodeEditor>("SqlEditor"));
        Assert.Equal(expectedSelectionStart, editorA.SelectionStart);
        Assert.Equal(expectedSelectionLength, editorA.SelectionLength);
        Assert.Equal(expectedCaret, editorA.CaretOffset);
        Assert.Equal(scrollA, editorA.TextArea.TextView.ScrollOffset);
        Assert.Equal(expectedBStart, editorB.SelectionStart);
        Assert.Equal(expectedBLength, editorB.SelectionLength);
    });

    [Fact]
    public Task TabReorder_RecreatesView_TransfersEditorTextToNewInstance() => RunOnUi(() =>
    {
        // Regression: before the fix, Dock Remove+Insert rebuilt SqlDocumentView with an empty
        // editor while _contentHydrated blocked reload — the tab appeared blank.
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);
        const string sql = "SELECT 'keep-me-after-reorder';";
        var vm = CreateSqlDocumentViewModel("doc-reorder", sql);

        var host = new ContentControl { Width = 640, Height = 480 };
        var window = new Window
        {
            Width = 700,
            Height = 520,
            Content = host,
            Title = "SQL reorder recreation"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view1 = Assert.IsType<SqlDocumentView>(template.Build(vm));
        view1.DataContext = vm;
        host.Content = view1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(sql, vm.SqlEditor.Text);
        vm.SqlEditor.Document.Text = sql + " -- edited";
        var expected = vm.SqlEditor.Text;

        // Dock MoveDockable is Remove+Insert → new container/view for the same VM.
        var view2 = Assert.IsType<SqlDocumentView>(template.Build(vm));
        Assert.NotSame(view1, view2);
        view2.DataContext = vm;
        host.Content = view2;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(view2.FindControl<SqlCodeEditor>("SqlEditor"), vm.SqlEditor);
        Assert.Equal(expected, vm.SqlEditor.Text);
        Assert.False(string.IsNullOrWhiteSpace(vm.SqlEditor.Text));
    });

    [Fact]
    public Task DockControl_FillOnlySqlTabs_CanSwitchActiveDocument() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var vmA = CreateSqlDocumentViewModel("dock-a", "SELECT 1;" + Environment.NewLine + string.Concat(Enumerable.Repeat("-- a" + Environment.NewLine, 40)));
        var vmB = CreateSqlDocumentViewModel("dock-b", "SELECT 2;" + Environment.NewLine + string.Concat(Enumerable.Repeat("-- b" + Environment.NewLine, 40)));

        var factory = new MinimalSqlDockFactory(vmA, vmB);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var documentDock = Assert.IsType<DocumentDock>(layout.ActiveDockable);
        Assert.True(vmA.CanDrag);
        Assert.False(vmA.CanFloat);
        Assert.Equal(DockOperationMask.Fill, vmA.AllowedDockOperations);
        Assert.Equal(DockOperationMask.Fill, documentDock.AllowedDropOperations);

        var dockControl = new DockControl
        {
            Layout = layout,
            Width = 640,
            Height = 480
        };

        var window = new Window
        {
            Width = 700,
            Height = 520,
            Content = dockControl,
            Title = "SQL DockControl switch"
        };
        window.DataTemplates.Add(new SqlDocumentDataTemplate(provider));
        window.Styles.Add(new DockFluentTheme { CacheDocumentTabContent = true });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        documentDock.ActiveDockable = vmA;
        factory.SetActiveDockable(vmA);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(vmA, documentDock.ActiveDockable);

        documentDock.ActiveDockable = vmB;
        factory.SetActiveDockable(vmB);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(vmB, documentDock.ActiveDockable);

        documentDock.ActiveDockable = vmA;
        factory.SetActiveDockable(vmA);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(vmA, documentDock.ActiveDockable);

        // Content cache + typed template path is covered by DocumentContentCache_*;
        // here we only require Fill-only Dock hosting and stable ActiveDockable switches.
        Assert.NotNull(window.CaptureRenderedFrame());
    });
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IMessageForUserTools>());
        services.AddSingleton<ISimpleLogger>(ISimpleLogger.EmptyLogger);
        return services.BuildServiceProvider();
    }

    private static SqlDocumentViewModel CreateSqlDocumentViewModel(string documentId, string sqlText)
    {
        OfflineTabData offlineTabData = new()
        {
            MyId = documentId,
            Title = documentId,
            SqlText = sqlText,
            SqlFilePath = null
        };

        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());
        appData.SetupGet(x => x.GetAllSnippets).Returns(new Dictionary<string, (string snippetType, string? Description, string? Text, string? Keyword)>());
        appData.SetupGet(x => x.FastReplaceDictionary).Returns(new Dictionary<string, string>());
        appData.SetupGet(x => x.TypoPatternList).Returns([]);
        appData.SetupProperty(x => x.VariablesDictionary, new Dictionary<string, string>());
        appData.SetupGet(x => x.CollapseFoldingOnStartup).Returns(false);
        appData.SetupGet(x => x.LoginDataDic).Returns(new Dictionary<string, LoginDataModel>());
        appData.Setup(x => x.GetDocumentsKeyValueCollection()).Returns(Array.Empty<KeyValuePair<string, OfflineTabData>>());
        appData.Setup(x => x.GetFormatterSql(It.IsAny<string>())).Returns<string>(text => text);
        appData.Setup(x => x.TryGetDocumentById(documentId, out offlineTabData)).Returns(true);

        var messageForUserTools = new Mock<IMessageForUserTools>();
        messageForUserTools
            .Setup(x => x.DispatcherActionInstance(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        var logToolViewModel = new LogToolViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IClipboardService>(),
            messageForUserTools.Object);

        var executionServices = new Mock<ISqlDocumentExecutionServices>();
        executionServices.SetupGet(x => x.ConnectionManager).Returns(Mock.Of<ISqlConnectionManager>());
        executionServices.SetupGet(x => x.ExecutionStateService).Returns(Mock.Of<ISqlExecutionStateService>(x => x.ActiveTasksCount == 0));

        return new SqlDocumentViewModel(
            Mock.Of<IFactory>(),
            appData.Object,
            new HistoryService(appData.Object),
            Mock.Of<ISqlCodeFormatterService>(),
            messageForUserTools.Object,
            ISimpleLogger.EmptyLogger,
            Mock.Of<ISqlVariableProcessor>(),
            logToolViewModel,
            Mock.Of<IDocumentCloseDecisionService>(),
            executionServices.Object,
            Mock.Of<ISqlDocumentInteractionServices>(),
            Mock.Of<ISqlDocumentUiServices>(),
            Mock.Of<IActiveDocumentManager>(),
            Mock.Of<ISqlResultManager>())
        {
            Id = documentId,
            Title = documentId,
            CanFloat = false,
            CanDrag = true,
            AllowedDockOperations = DockOperationMask.Fill
        };
    }

    private sealed class MinimalSqlDockFactory(SqlDocumentViewModel docA, SqlDocumentViewModel docB) : Factory
    {
        public override IRootDock CreateLayout()
        {
            var documentDock = new DocumentDock
            {
                Id = "Documents",
                Title = "Documents",
                IsCollapsable = false,
                CanCreateDocument = false,
                CanFloat = false,
                CanDrop = true,
                AllowedDropOperations = DockOperationMask.Fill,
                VisibleDockables = CreateList<IDockable>(docA, docB),
                ActiveDockable = docA
            };

            var root = CreateRootDock();
            root.Id = "Root";
            root.VisibleDockables = CreateList<IDockable>(documentDock);
            root.ActiveDockable = documentDock;
            root.DefaultDockable = documentDock;
            return root;
        }
    }
}
