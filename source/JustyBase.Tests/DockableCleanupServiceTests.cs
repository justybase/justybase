using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

public sealed class DockableCleanupServiceTests
{
    [Fact]
    public void CleanupDockable_WhenDockableIsCleanable_InvokesCleanup()
    {
        var dockable = new Mock<IDockable>();
        var cleanable = dockable.As<ICleanableViewModel>();
        var sut = new DockableCleanupService();

        sut.CleanupDockable(dockable.Object, null);

        cleanable.Verify(x => x.DoCleanup(), Times.Once);
    }

    [Fact]
    public void CleanupDockable_WhenDockableIsDisposable_DisposesIt()
    {
        var dockable = new Mock<IDockable>();
        var disposable = dockable.As<IDisposable>();
        var sut = new DockableCleanupService();

        sut.CleanupDockable(dockable.Object, null);

        disposable.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public void CleanupDockable_WhenDockableIsSqlDocument_ClearsRelatedResults()
    {
        var offlineTabData = CreateOfflineTabData("doc-1", string.Empty);
        var dockable = CreateSqlDocumentViewModel("doc-1", offlineTabData);
        string? clearedDocumentId = null;
        var sut = new DockableCleanupService();

        sut.CleanupDockable(dockable, documentId => clearedDocumentId = documentId);

        Assert.Equal("doc-1", clearedDocumentId);
    }

    [Fact]
    public void CleanupDockable_WhenDockableIsNotSqlDocument_DoesNotClearResults()
    {
        var dockable = new Mock<IDockable>();
        bool clearResultsCalled = false;
        var sut = new DockableCleanupService();

        sut.CleanupDockable(dockable.Object, _ => clearResultsCalled = true);

        Assert.False(clearResultsCalled);
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

    private static OfflineTabData CreateOfflineTabData(string id, string? sqlText, string? sqlFilePath = null)
    {
        return new OfflineTabData
        {
            MyId = id,
            Title = "Test document",
            SqlText = sqlText,
            SqlFilePath = sqlFilePath,
            HotDocumentViewModel = null
        };
    }
}
