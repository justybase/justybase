using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Docking;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

public sealed class DockSqlDocumentFactoryTests
{
    [Fact]
    public void CreateDocument_SetsExplicitPropertiesAndStoresHotDocument()
    {
        OfflineTabData offlineTabData = CreateOfflineTabData("doc-1");
        var appData = new Mock<IGeneralApplicationData>();
        appData.Setup(x => x.AddNewDocument("script.sql", "SELECT 1;")).Returns("doc-1");
        appData.Setup(x => x.GetDocumentVmById("doc-1")).Returns(offlineTabData);

        SqlDocumentViewModel sqlDocumentViewModel = CreateSqlDocumentViewModel("doc-1", offlineTabData);
        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlDocumentViewModel()).Returns(sqlDocumentViewModel);

        var sut = new DockSqlDocumentFactory(appData.Object, viewModelFactory.Object);

        SqlDocumentViewModel result = sut.CreateDocument(
            "script.sql",
            initText: "SELECT 1;",
            txtPreview: true,
            filePath: @"C:\tmp\script.sql",
            fontSize: 21);

        Assert.Same(sqlDocumentViewModel, result);
        Assert.Equal("doc-1", result.Id);
        Assert.Equal("script.sql", result.Title);
        Assert.True(result.TxtPreview);
        Assert.Equal(@"C:\tmp\script.sql", result.FilePath);
        Assert.Equal(21, result.FontSize);
        Assert.Same(result, offlineTabData.HotDocumentViewModel);
    }

    [Fact]
    public void CreateDocument_WhenFontSizeIsNotProvided_UsesOfflineDocumentFontSize()
    {
        OfflineTabData offlineTabData = CreateOfflineTabData("doc-2");
        offlineTabData.FontSize = 17;

        var appData = new Mock<IGeneralApplicationData>();
        appData.Setup(x => x.AddNewDocument("Document2", null)).Returns("doc-2");
        appData.Setup(x => x.GetDocumentVmById("doc-2")).Returns(offlineTabData);

        SqlDocumentViewModel sqlDocumentViewModel = CreateSqlDocumentViewModel("doc-2", offlineTabData);
        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlDocumentViewModel()).Returns(sqlDocumentViewModel);

        var sut = new DockSqlDocumentFactory(appData.Object, viewModelFactory.Object);

        SqlDocumentViewModel result = sut.CreateDocument("Document2");

        Assert.Equal(17, result.FontSize);
        Assert.False(result.TxtPreview);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void CreateDocument_WithNullInitText_DoesNotInjectSqlTextFromOtherDocument()
    {
        OfflineTabData offlineTabData = CreateOfflineTabData("doc-3");
        string? capturedInitText = "seed";

        var appData = new Mock<IGeneralApplicationData>();
        appData
            .Setup(x => x.AddNewDocument("Document3", It.IsAny<string?>()))
            .Callback<string, string?>((_, initText) => capturedInitText = initText)
            .Returns("doc-3");
        appData.Setup(x => x.GetDocumentVmById("doc-3")).Returns(offlineTabData);

        SqlDocumentViewModel sqlDocumentViewModel = CreateSqlDocumentViewModel("doc-3", offlineTabData);
        var viewModelFactory = new Mock<IDockViewModelFactory>();
        viewModelFactory.Setup(x => x.CreateSqlDocumentViewModel()).Returns(sqlDocumentViewModel);

        var sut = new DockSqlDocumentFactory(appData.Object, viewModelFactory.Object);

        _ = sut.CreateDocument("Document3", initText: null);

        Assert.Null(capturedInitText);
        Assert.Null(offlineTabData.SqlText);
    }

    private static SqlDocumentViewModel CreateSqlDocumentViewModel(string documentId, OfflineTabData offlineTabData)
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
        appData.Setup(x => x.TryGetDocumentById(documentId, out offlineTabData)).Returns(true);

        var messageForUserTools = new Mock<IMessageForUserTools>();
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
            Title = "Test document"
        };
    }

    private static OfflineTabData CreateOfflineTabData(string id)
    {
        return new OfflineTabData
        {
            MyId = id,
            Title = "Test document",
            SqlText = null,
            SqlFilePath = null
        };
    }
}
