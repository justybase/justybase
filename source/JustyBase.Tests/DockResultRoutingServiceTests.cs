using System.Data;
using System.Data.Common;
using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.Helpers.Interactions;
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

public sealed class DockResultRoutingServiceTests
{
    [Fact]
    public void AddResult_WhenReaderHasNoRows_AddsConfiguredResultAndEnablesGrid()
    {
        var messageForUserTools = CreateMessageForUserTools();
        SqlDocumentViewModel document = CreateSqlDocumentViewModel("doc-1", "Query 1");
        SqlResultsFastViewModel resultsHost = CreateSqlResultsFastViewModel(messageForUserTools.Object);
        SqlResultsViewModel resultViewModel = CreateSqlResultsViewModel(messageForUserTools.Object);

        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlResultsViewModel()).Returns(resultViewModel);

        var reader = new Mock<DbDataReader>();
        reader.SetupGet(x => x.HasRows).Returns(false);
        reader.SetupGet(x => x.FieldCount).Returns(0);

        int abortUpperBound = 5;
        var sut = new DockResultRoutingService(viewModelFactory.Object, messageForUserTools.Object);

        sut.AddResult(
            (Mock.Of<IDatabaseService>(), reader.Object, string.Empty),
            "doc-1",
            document,
            queryNum: 1,
            ref abortUpperBound,
            sql: "SELECT 1;",
            command: null,
            title: null,
            resultsHost,
            isDocumentActive: false);

        SqlResultsViewModel addedResult = Assert.Single(resultsHost.SqlResultsViewModels);
        Assert.Equal("doc-1", addedResult.RelatedSqlDocumentId);
        Assert.Equal("Query 1", addedResult.Title);
        Assert.Equal("SELECT 1;", addedResult.SQL);
        Assert.True(addedResult.GridEnabled);
        Assert.False(addedResult.DataLoadingInProgress);
        Assert.False(addedResult.IsResultVisible);
    }

    [Fact]
    public void AddResult_WhenSchemaOnlyEmptyResult_ClearsLoadingIndicator()
    {
        var messageForUserTools = CreateMessageForUserTools();
        SqlDocumentViewModel document = CreateSqlDocumentViewModel("doc-empty", "Empty query");
        SqlResultsFastViewModel resultsHost = CreateSqlResultsFastViewModel(messageForUserTools.Object);
        SqlResultsViewModel resultViewModel = CreateSqlResultsViewModel(messageForUserTools.Object);

        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlResultsViewModel()).Returns(resultViewModel);

        var reader = new Mock<DbDataReader>();
        reader.SetupGet(x => x.HasRows).Returns(false);
        reader.SetupGet(x => x.FieldCount).Returns(2);
        reader.Setup(x => x.GetName(0)).Returns("DATEKEY");
        reader.Setup(x => x.GetName(1)).Returns("FULLDATEALTERNATEKEY");
        reader.Setup(x => x.GetDataTypeName(It.IsAny<int>())).Returns("INTEGER");
        reader.Setup(x => x.GetFieldType(It.IsAny<int>())).Returns(typeof(int));
        reader.Setup(x => x.GetSchemaTable()).Returns((DataTable?)null);
        reader.Setup(x => x.Read()).Returns(false);

        var dbService = new Mock<IDatabaseService>();
        dbService.Setup(x => x.GetDatabaseRowReader(It.IsAny<DbDataReader>()))
            .Returns(Mock.Of<IDatabaseRowReader>());

        int abortUpperBound = 1;
        var sut = new DockResultRoutingService(viewModelFactory.Object, messageForUserTools.Object);

        sut.AddResult(
            (dbService.Object, reader.Object, string.Empty),
            "doc-empty",
            document,
            queryNum: 1,
            ref abortUpperBound,
            sql: "SELECT * FROM DIMDATE WHERE 1=2",
            command: Mock.Of<DbCommand>(),
            title: null,
            resultsHost,
            isDocumentActive: true);

        SqlResultsViewModel addedResult = Assert.Single(resultsHost.SqlResultsViewModels);
        Assert.True(addedResult.GridEnabled);
        Assert.False(addedResult.DataLoadingInProgress);
        Assert.Equal("0 rows", addedResult.RowsLoadingMessage);
        Assert.Equal(2, addedResult.CurrentResultsTable.Headers.Count);
    }

    [Fact]
    public void SyncActiveDocumentResults_ShowsDocumentResultsAndSwitchesLogs()
    {
        var messageForUserTools = CreateMessageForUserTools();
        SqlDocumentViewModel document = CreateSqlDocumentViewModel("doc-2", "Query 2");
        SqlResultsFastViewModel resultsHost = CreateSqlResultsFastViewModel(messageForUserTools.Object);
        SqlResultsViewModel resultViewModel = CreateSqlResultsViewModel(messageForUserTools.Object);

        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlResultsViewModel()).Returns(resultViewModel);

        var reader = new Mock<DbDataReader>();
        reader.SetupGet(x => x.HasRows).Returns(false);
        reader.SetupGet(x => x.FieldCount).Returns(0);

        int abortUpperBound = 1;
        var sut = new DockResultRoutingService(viewModelFactory.Object, messageForUserTools.Object);

        sut.AddResult(
            (Mock.Of<IDatabaseService>(), reader.Object, string.Empty),
            "doc-2",
            document,
            queryNum: 1,
            ref abortUpperBound,
            sql: "SELECT 2;",
            command: null,
            title: "Result 2",
            resultsHost,
            isDocumentActive: false);

        string? switchedLogId = null;

        sut.SyncActiveDocumentResults(document, resultsHost, id => switchedLogId = id);

        SqlResultsFastTile resultTitle = Assert.Single(resultsHost.SqlResultsTitles);
        Assert.True(resultTitle.IsTitleVisible);
        Assert.True(resultTitle.ReferencedSqlResult.IsResultVisible);
        Assert.Equal("doc-2", switchedLogId);
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

    private static SqlResultsFastViewModel CreateSqlResultsFastViewModel(IMessageForUserTools messageForUserTools)
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());
        return new SqlResultsFastViewModel(Mock.Of<IFactory>(), appData.Object, messageForUserTools);
    }

    private static SqlResultsViewModel CreateSqlResultsViewModel(IMessageForUserTools messageForUserTools)
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());
        return new SqlResultsViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IClipboardService>(),
            appData.Object,
            messageForUserTools,
            ISimpleLogger.EmptyLogger,
            Mock.Of<IResultGridActionRoutingService>(),
            Mock.Of<IActiveDocumentManager>());
    }

    private static SqlDocumentViewModel CreateSqlDocumentViewModel(string documentId, string title)
    {
        OfflineTabData offlineTabData = new()
        {
            MyId = documentId,
            Title = title,
            SqlText = "SELECT 1;",
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
            Mock.Of<ISqlDocumentUiServices>(),
            Mock.Of<IActiveDocumentManager>(),
            Mock.Of<ISqlResultManager>())
        {
            Id = documentId,
            Title = title
        };
    }
}
