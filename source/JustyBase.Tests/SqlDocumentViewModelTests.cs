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
using System.Collections;
using System.Reflection;

namespace JustyBase.Tests;

public sealed class SqlDocumentViewModelTests
{
    [Fact]
    public void InserSnippet_WhenEditorIsNotReadyAndTextHasNoPlaceholders_StoresTextInOfflineState()
    {
        var offlineTabData = CreateOfflineTabData("doc-1", sqlText: string.Empty);
        var viewModel = CreateViewModel("doc-1", offlineTabData);

        viewModel.InserSnippet("CREATE TABLE test_table(id INT);");

        Assert.Equal("CREATE TABLE test_table(id INT);", offlineTabData.SqlText);
        Assert.Null(offlineTabData.SqlFilePath);
    }

    [Fact]
    public void InserSnippet_WhenEditorIsNotReadyAndTextHasPlaceholders_QueuesSnippetInsteadOfPersistingRawText()
    {
        var offlineTabData = CreateOfflineTabData("doc-queue", sqlText: string.Empty);
        var viewModel = CreateViewModel("doc-queue", offlineTabData);

        viewModel.InserSnippet("SELECT ${ALIAS=T1}.COL1 FROM MY_TABLE ${ALIAS}${Caret};");

        Assert.Equal(string.Empty, offlineTabData.SqlText);

        var pendingField = typeof(SqlDocumentViewModel).GetField("_pendingSnippetTexts", BindingFlags.Instance | BindingFlags.NonPublic);
        var pendingQueue = Assert.IsAssignableFrom<ICollection>(pendingField?.GetValue(viewModel));

        Assert.Equal(1, pendingQueue.Count);
    }

    [Fact]
    public void InsertTextRequest_WhenEditorIsNotReadyAndDocumentIsFileBacked_LoadsFileTextBeforeAppend()
    {
        var tempFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFilePath, "SELECT 1;");
            var offlineTabData = CreateOfflineTabData("doc-2", sqlText: null, sqlFilePath: tempFilePath);
            var viewModel = CreateViewModel("doc-2", offlineTabData);

            viewModel.InsertTextRequest(Environment.NewLine + "SELECT 2;", rawMode: true);

            Assert.Equal("SELECT 1;" + Environment.NewLine + "SELECT 2;", offlineTabData.SqlText);
            Assert.Null(offlineTabData.SqlFilePath);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    private static SqlDocumentViewModel CreateViewModel(string documentId, OfflineTabData offlineTabData)
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
            new Mock<IClipboardService>().Object,
            messageForUserTools.Object);

        var executionServices = new Mock<ISqlDocumentExecutionServices>();
        executionServices.SetupGet(x => x.ConnectionManager).Returns(Mock.Of<ISqlConnectionManager>());
        executionServices.SetupGet(x => x.ExecutionStateService).Returns(Mock.Of<ISqlExecutionStateService>(x => x.ActiveTasksCount == 0));

        var viewModel = new SqlDocumentViewModel(
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

        return viewModel;
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
