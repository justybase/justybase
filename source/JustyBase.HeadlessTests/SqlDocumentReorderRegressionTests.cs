using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// Regression tests for the empty-SQL-after-tab-reorder bug.
///
/// Pre-fix behavior (both must be covered — either caused a blank editor after drag reorder):
/// 1. Dock MoveDockable does Remove+Insert on VisibleDockables → new SqlDocumentView / empty SqlCodeEditor.
/// 2. _contentHydrated stayed true, so OnSqlEditorChanged skipped HydrateEditorContent and did not
///    copy text from the previous editor → visible document was empty.
/// 3. SqlDocumentView refused to rebind SqlEditor when the VM already pointed at the orphaned editor,
///    so the new empty view stayed detached from the VM.
/// </summary>
public sealed class SqlDocumentReorderRegressionTests : HeadlessSessionTestBase
{
    private const string LiveSql = "SELECT 'unsaved-edit-must-survive-reorder';";

    /// <summary>
    /// Direct reproduction of failure mode (2): after first hydrate, assigning a brand-new empty
    /// editor used to leave the document blank because hydrate was skipped and text was not transferred.
    /// </summary>
    [Fact]
    public Task ReplacingHydratedEditor_WithEmptyInstance_KeepsLiveText() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);
        var vm = CreateSqlDocumentViewModel("doc-editor-swap", "SELECT 1;");

        var firstView = BindInWindow(template, vm, "editor-swap-1");
        var firstEditor = firstView.FindControl<SqlCodeEditor>("SqlEditor");
        Assert.NotNull(firstEditor);
        firstEditor.Document.Text = LiveSql;
        firstEditor.Select(8, 6);
        firstEditor.CaretOffset = 8;
        var expectedStart = firstEditor.SelectionStart;
        var expectedLength = firstEditor.SelectionLength;
        var expectedCaret = firstEditor.CaretOffset;
        Assert.Equal(LiveSql, vm.SqlEditor.Text);

        // Simulate Dock recreating the document content control after Remove+Insert.
        var secondEditor = new SqlCodeEditor();
        Assert.True(string.IsNullOrEmpty(secondEditor.Text));

        vm.SqlEditor = secondEditor;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(secondEditor, vm.SqlEditor);
        Assert.Equal(LiveSql, secondEditor.Text);
        Assert.Equal(expectedStart, secondEditor.SelectionStart);
        Assert.Equal(expectedLength, secondEditor.SelectionLength);
        Assert.Equal(expectedCaret, secondEditor.CaretOffset);
        Assert.False(string.IsNullOrWhiteSpace(vm.SqlEditor.Text));
    });

    [Fact]
    public Task ReplacingHydratedEditor_DoesNotReloadStaleOfflineText() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);
        var vm = CreateSqlDocumentViewModel("doc-offline-stale", "SELECT 'stale-offline';");

        BindInWindow(template, vm, "offline-stale");
        const string live = "SELECT 'live-unsaved';";
        vm.SqlEditor.Document.Text = live;

        vm.SqlEditor = new SqlCodeEditor();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(live, vm.SqlEditor.Text);
        Assert.DoesNotContain("stale-offline", vm.SqlEditor.Text, StringComparison.Ordinal);
    });

    /// <summary>
    /// Direct reproduction of failure mode (3): a second SqlDocumentView for the same VM used to
    /// skip SqlEditor rebinding (VM already had the old editor), so the visible control stayed empty.
    /// </summary>
    [Fact]
    public Task SecondViewForSameVm_RebindsEditorAndKeepsText() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);
        var vm = CreateSqlDocumentViewModel("doc-second-view", "SELECT 1;");

        var host = new ContentControl { Width = 640, Height = 480 };
        var window = new Window
        {
            Width = 700,
            Height = 520,
            Content = host,
            Title = "second-view-rebind"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view1 = Assert.IsType<SqlDocumentView>(template.Build(vm));
        view1.DataContext = vm;
        host.Content = view1;
        Dispatcher.UIThread.RunJobs();

        var editor1 = view1.FindControl<SqlCodeEditor>("SqlEditor");
        Assert.NotNull(editor1);
        Assert.Same(editor1, vm.SqlEditor);
        editor1.Document.Text = LiveSql;

        var view2 = Assert.IsType<SqlDocumentView>(template.Build(vm));
        Assert.NotSame(view1, view2);
        var editor2 = view2.FindControl<SqlCodeEditor>("SqlEditor");
        Assert.NotNull(editor2);
        Assert.NotSame(editor1, editor2);
        Assert.True(string.IsNullOrEmpty(editor2.Text));

        // Pre-fix: DataContextChanged refused to assign because vm.SqlEditor == editor1.
        view2.DataContext = vm;
        host.Content = view2;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(editor2, vm.SqlEditor);
        Assert.Equal(LiveSql, editor2.Text);
        Assert.Equal(LiveSql, vm.SqlEditor.Text);
    });

    /// <summary>
    /// End-to-end Dock path: MoveDockable Remove+Insert must not empty the dragged SQL document.
    /// Before the fix this left the active tab blank after reorder.
    /// </summary>
    [Fact]
    public Task MoveDockable_DoesNotEmptyDraggedSqlDocument() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var vmA = CreateSqlDocumentViewModel("dock-move-a", "SELECT 'A';");
        var vmB = CreateSqlDocumentViewModel("dock-move-b", "SELECT 'B';");

        var factory = new MinimalSqlDockFactory(vmA, vmB);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var documentDock = Assert.IsType<DocumentDock>(layout.ActiveDockable);

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
            Title = "move-dockable-reorder"
        };
        window.DataTemplates.Add(new SqlDocumentDataTemplate(provider));
        window.Styles.Add(new DockFluentTheme { CacheDocumentTabContent = true });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Materialize A, then edit live text (offline SqlText is stale until session save).
        documentDock.ActiveDockable = vmA;
        factory.SetActiveDockable(vmA);
        Dispatcher.UIThread.RunJobs();

        var viewABefore = FindSqlDocumentView(dockControl, vmA);
        if (viewABefore is not null)
        {
            var editorBefore = viewABefore.FindControl<SqlCodeEditor>("SqlEditor");
            Assert.NotNull(editorBefore);
            editorBefore.Document.Text = LiveSql;
        }
        else
        {
            // If Dock has not materialized content yet, bind through the template the same way
            // the app would after first activation, then force the live edit onto the VM editor.
            var seeded = Assert.IsType<SqlDocumentView>(new SqlDocumentDataTemplate(provider).Build(vmA));
            seeded.DataContext = vmA;
            Dispatcher.UIThread.RunJobs();
            vmA.SqlEditor.Document.Text = LiveSql;
        }

        Assert.Equal(LiveSql, vmA.SqlEditor.Text);

        // Exact Dock reorder operation that emptied tabs before the fix.
        factory.MoveDockable(documentDock, vmA, vmB);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(vmA, documentDock.ActiveDockable);
        Assert.False(string.IsNullOrWhiteSpace(vmA.SqlEditor.Text));
        Assert.Equal(LiveSql, vmA.SqlEditor.Text);

        var viewAAfter = FindSqlDocumentView(dockControl, vmA);
        if (viewAAfter is not null)
        {
            var editorAfter = viewAAfter.FindControl<SqlCodeEditor>("SqlEditor");
            Assert.NotNull(editorAfter);
            Assert.Equal(LiveSql, editorAfter.Text);
        }
    });

    /// <summary>
    /// Transfer must set Document.Text before Initialize wires Document.Changed (WarmCache).
    /// Otherwise tab reorder re-triggers application handlers on a non-user edit.
    /// </summary>
    [Fact]
    public Task ReplacingHydratedEditor_TransferHappensBeforeInitializeHandlers() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);
        var vm = CreateSqlDocumentViewModel("doc-transfer-order", "SELECT 1;");

        BindInWindow(template, vm, "transfer-order");
        vm.SqlEditor.Document.Text = LiveSql;

        var secondEditor = new SqlCodeEditor();
        var initializedField = typeof(SqlCodeEditor).GetField(
            "_editorServicesInitialized",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected _editorServicesInitialized field.");

        var changedWhileUninitialized = 0;
        var changedWhileInitialized = 0;
        secondEditor.Document.Changed += (_, _) =>
        {
            var initialized = (bool)initializedField.GetValue(secondEditor)!;
            if (initialized)
            {
                changedWhileInitialized++;
            }
            else
            {
                changedWhileUninitialized++;
            }
        };

        vm.SqlEditor = secondEditor;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(LiveSql, secondEditor.Text);
        Assert.True(changedWhileUninitialized >= 1,
            "Document.Text transfer should fire Changed before Initialize.");
        Assert.Equal(0, changedWhileInitialized);
    });

    /// <summary>
    /// Large-document transfer after reorder must stay cheap: text is copied before
    /// Initialize / linter attach, so no redundant parse/lint cycle rides on Document.Text.
    /// </summary>
    [Fact]
    public Task ReplacingHydratedEditor_LargeDocumentTransfer_StaysWithinBudget() => RunOnUi(() =>
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);
        var vm = CreateSqlDocumentViewModel("doc-transfer-perf", "SELECT 1;");

        BindInWindow(template, vm, "transfer-perf");

        // ~100K chars of SQL-ish content (comments + a final statement).
        var largeSql = string.Concat(
            Enumerable.Repeat("-- line " + new string('x', 80) + Environment.NewLine, 1_200))
            + "SELECT 1;";
        Assert.True(largeSql.Length >= 100_000, $"Expected >=100K chars, got {largeSql.Length}.");
        vm.SqlEditor.Document.Text = largeSql;

        var secondEditor = new SqlCodeEditor();
        var sw = Stopwatch.StartNew();
        vm.SqlEditor = secondEditor;
        sw.Stop();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(largeSql, secondEditor.Text);
        Assert.True(
            sw.ElapsedMilliseconds < 50,
            $"Large transfer took {sw.ElapsedMilliseconds}ms (budget 50ms). Length={largeSql.Length}.");
    });

    private static SqlDocumentView BindInWindow(SqlDocumentDataTemplate template, SqlDocumentViewModel vm, string title)
    {
        var view = Assert.IsType<SqlDocumentView>(template.Build(vm));
        var window = new Window
        {
            Width = 700,
            Height = 520,
            Content = view,
            Title = title
        };
        view.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static SqlDocumentView? FindSqlDocumentView(Control root, SqlDocumentViewModel vm) =>
        root.GetVisualDescendants()
            .OfType<SqlDocumentView>()
            .FirstOrDefault(v => ReferenceEquals(v.DataContext, vm));

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
