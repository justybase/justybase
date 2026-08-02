using System.Reflection;
using System.Runtime.CompilerServices;
using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Editor;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.Services.Docking;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlDocumentActivationTests
{
    [Fact]
    public void OnActivated_FocusesEditorViaUiServices()
    {
        var ui = new Mock<ISqlDocumentUiServices>();
        var vm = CreateSqlDocumentViewModel("doc-focus", ui.Object);
        var editor = CreateUninitializedEditor();
        SetSqlEditorField(vm, editor);

        vm.OnActivated();

        ui.Verify(x => x.FocusEditorOnSelectedTab(editor), Times.Once);
    }

    [Fact]
    public void OnActivated_WhenEditorIsNull_StillCallsFocusWithNull()
    {
        var ui = new Mock<ISqlDocumentUiServices>();
        var vm = CreateSqlDocumentViewModel("doc-null-editor", ui.Object);

        vm.OnActivated();

        ui.Verify(x => x.FocusEditorOnSelectedTab(null), Times.Once);
    }

    [Fact]
    public void SyncActiveDocumentResults_ShowsResultsAndSwitchesLogsForActivatedDocument()
    {
        var messageForUserTools = CreateMessageForUserTools();
        var document = CreateSqlDocumentViewModel("doc-results", Mock.Of<ISqlDocumentUiServices>());
        document.Title = "Query A";

        var resultsHost = new SqlResultsFastViewModel(
            Mock.Of<IFactory>(),
            CreateAppData().Object,
            messageForUserTools.Object);

        var resultViewModel = new SqlResultsViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IClipboardService>(),
            CreateAppData().Object,
            messageForUserTools.Object,
            ISimpleLogger.EmptyLogger,
            Mock.Of<IResultGridActionRoutingService>(),
            Mock.Of<IActiveDocumentManager>())
        {
            Id = "result-1",
            RelatedSqlDocumentId = "doc-results",
            Title = "Query A"
        };

        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlResultsViewModel()).Returns(resultViewModel);

        var routing = new DockResultRoutingService(viewModelFactory.Object, messageForUserTools.Object);
        var reader = new Mock<System.Data.Common.DbDataReader>();
        reader.SetupGet(x => x.HasRows).Returns(false);
        reader.SetupGet(x => x.FieldCount).Returns(0);

        int abortUpperBound = 1;
        routing.AddResult(
            (Mock.Of<IDatabaseService>(), reader.Object, string.Empty),
            "doc-results",
            document,
            queryNum: 1,
            ref abortUpperBound,
            sql: "SELECT 1;",
            command: null,
            title: "Result A",
            resultsHost,
            isDocumentActive: false);

        string? switchedLogId = null;
        routing.SyncActiveDocumentResults(document, resultsHost, id => switchedLogId = id);

        Assert.Equal("doc-results", switchedLogId);
        var title = Assert.Single(resultsHost.SqlResultsTitles);
        Assert.True(title.IsTitleVisible);
        Assert.True(title.ReferencedSqlResult.IsResultVisible);
    }

    [Fact]
    public void ClearRecentlyFinished_ThenOnActivated_PreparesDocumentForExecution()
    {
        var ui = new Mock<ISqlDocumentUiServices>();
        var vm = CreateSqlDocumentViewModel("doc-recent", ui.Object);
        vm.IsRecentlyFinished = true;
        var editor = CreateUninitializedEditor();
        SetSqlEditorField(vm, editor);

        // Mirrors MainWindowViewModel.HandleActiveDockableChanged
        if (vm.IsRecentlyFinished)
        {
            vm.IsRecentlyFinished = false;
        }

        vm.OnActivated();

        Assert.False(vm.IsRecentlyFinished);
        ui.Verify(x => x.FocusEditorOnSelectedTab(editor), Times.Once);
    }

    private static SqlCodeEditor CreateUninitializedEditor() =>
        (SqlCodeEditor)RuntimeHelpers.GetUninitializedObject(typeof(SqlCodeEditor));

    private static void SetSqlEditorField(SqlDocumentViewModel vm, SqlCodeEditor editor)
    {
        var field = typeof(SqlDocumentViewModel).GetField("<SqlEditor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected SqlEditor backing field.");
        field.SetValue(vm, editor);
    }

    private static Mock<IMessageForUserTools> CreateMessageForUserTools()
    {
        var messageForUserTools = new Mock<IMessageForUserTools>();
        messageForUserTools
            .Setup(x => x.DispatcherActionInstance(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        messageForUserTools
            .Setup(x => x.DispatcherActionInstance(It.IsAny<Action>(), It.IsAny<object>()))
            .Callback<Action, object>((action, _) => action());
        return messageForUserTools;
    }

    private static Mock<IGeneralApplicationData> CreateAppData(string? documentId = null, OfflineTabData? offlineTabData = null)
    {
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
        if (documentId is not null && offlineTabData is not null)
        {
            appData.Setup(x => x.TryGetDocumentById(documentId, out offlineTabData)).Returns(true);
        }

        return appData;
    }

    private static SqlDocumentViewModel CreateSqlDocumentViewModel(string documentId, ISqlDocumentUiServices uiServices)
    {
        OfflineTabData offlineTabData = new()
        {
            MyId = documentId,
            Title = "Test document",
            SqlText = "SELECT 1;",
            SqlFilePath = null
        };

        var appData = CreateAppData(documentId, offlineTabData);
        var messageForUserTools = CreateMessageForUserTools();
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
            uiServices,
            Mock.Of<IActiveDocumentManager>(),
            Mock.Of<ISqlResultManager>())
        {
            Id = documentId,
            Title = "Test document"
        };
    }
}
