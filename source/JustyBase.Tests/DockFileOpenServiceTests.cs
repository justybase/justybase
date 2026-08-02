using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Helpers;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.Docking;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

public sealed class DockFileOpenServiceTests
{
    [Fact]
    public void PrepareDocuments_WhenDocumentIsAlreadyOpen_ReusesExistingDocument()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sql");
        SqlDocumentViewModel existingDocument = CreateSqlDocumentViewModel("doc-1", "Existing");
        IHotDocumentVm? openedVm = existingDocument;
        List<IDockable> visibleDockables = [existingDocument];

        var appData = new Mock<IGeneralApplicationData>();
        appData.Setup(x => x.TryGetOpenedDocumentVmByFilePath(filePath, out openedVm)).Returns(true);

        var sut = new DockFileOpenService(appData.Object, Mock.Of<IOtherHelpers>(), Mock.Of<IDockSqlDocumentFactory>());

        SqlDocumentViewModel? result = sut.PrepareDocuments([filePath], visibleDockables, out IReadOnlyList<SqlDocumentViewModel> toDock);

        Assert.Same(existingDocument, result);
        Assert.Empty(toDock);
        Assert.Single(visibleDockables);
    }

    [Fact]
    public void PrepareDocuments_WhenFileIsLarge_CreatesPreviewDocument()
    {
        string filePath = CreateTempFile(length: 20L * 1024L * 1024L);
        try
        {
            IHotDocumentVm? openedVm = null;
            List<IDockable> visibleDockables = [new Document { Id = "existing" }];
            SqlDocumentViewModel previewDocument = CreateSqlDocumentViewModel("preview", "Document2");

            var appData = new Mock<IGeneralApplicationData>();
            appData.Setup(x => x.TryGetOpenedDocumentVmByFilePath(filePath, out openedVm)).Returns(false);

            var otherHelpers = new Mock<IOtherHelpers>();
            otherHelpers.Setup(x => x.CsvTxtPreviewer(filePath)).Returns("preview-text");

            var documentFactory = new Mock<IDockSqlDocumentFactory>();
            documentFactory
                .Setup(x => x.CreateDocument("Document2", "preview-text", true, null, null))
                .Returns(previewDocument);

            var sut = new DockFileOpenService(appData.Object, otherHelpers.Object, documentFactory.Object);

            SqlDocumentViewModel? result = sut.PrepareDocuments([filePath], visibleDockables, out IReadOnlyList<SqlDocumentViewModel> toDock);

            Assert.Same(previewDocument, result);
            Assert.Same(previewDocument, Assert.Single(toDock));
            Assert.Single(visibleDockables);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void PrepareDocuments_WhenFileIsRegular_CreatesStandardDocumentWithoutDocking()
    {
        string filePath = CreateTempFile(length: 128);
        try
        {
            IHotDocumentVm? openedVm = null;
            List<IDockable> visibleDockables = [];
            string title = Path.GetFileName(filePath);
            SqlDocumentViewModel openedDocument = CreateSqlDocumentViewModel("doc-2", title);

            var appData = new Mock<IGeneralApplicationData>();
            appData.Setup(x => x.TryGetOpenedDocumentVmByFilePath(filePath, out openedVm)).Returns(false);

            var documentFactory = new Mock<IDockSqlDocumentFactory>();
            documentFactory
                .Setup(x => x.CreateDocument(title, null, false, filePath, ISomeEditorOptions.DEFAULT_DOCUMENT_FONT_SIZE))
                .Returns(openedDocument);

            var sut = new DockFileOpenService(appData.Object, Mock.Of<IOtherHelpers>(), documentFactory.Object);

            SqlDocumentViewModel? result = sut.PrepareDocuments([filePath], visibleDockables, out IReadOnlyList<SqlDocumentViewModel> toDock);

            Assert.Same(openedDocument, result);
            Assert.Same(openedDocument, Assert.Single(toDock));
            Assert.Empty(visibleDockables);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CreateTempFile(long length)
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sql");
        using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        return filePath;
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
            Title = title
        };
    }
}
