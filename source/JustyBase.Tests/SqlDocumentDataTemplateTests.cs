using Avalonia.Controls;
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
using JustyBase.Views.Documents;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JustyBase.Tests;

/// <summary>
/// Non-UI checks for SQL document templating and Dock caps.
/// View construction / editor transfer live in JustyBase.HeadlessTests (dispatcher-affine).
/// </summary>
public sealed class SqlDocumentDataTemplateTests
{
    [Fact]
    public void Match_ReturnsTrueOnlyForSqlDocumentViewModel()
    {
        using var provider = CreateProvider();
        var template = new SqlDocumentDataTemplate(provider);

        Assert.True(template.Match(CreateSqlDocumentViewModel("doc-match")));
        Assert.False(template.Match(new object()));
        Assert.False(template.Match(null));
    }

    [Fact]
    public void Ctor_ConfiguresFillOnlyTabReorderCapabilities()
    {
        var vm = CreateSqlDocumentViewModel("doc-caps");

        Assert.True(vm.CanDrag);
        Assert.False(vm.CanFloat);
        Assert.Equal(DockOperationMask.Fill, vm.AllowedDockOperations);
    }

    [Fact]
    public void ViewLocator_Build_SqlDocumentViewModel_DoesNotReturnSqlDocumentView()
    {
        using var provider = CreateProvider();
        var locator = new ViewLocator(provider);
        var vm = CreateSqlDocumentViewModel("doc-locator-fallback");

        var result = locator.Build(vm);

        Assert.IsNotType<SqlDocumentView>(result);
        var block = Assert.IsType<TextBlock>(result);
        Assert.Contains("SqlDocumentDataTemplate", block.Text, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IMessageForUserTools>());
        services.AddSingleton<ISimpleLogger>(ISimpleLogger.EmptyLogger);
        return services.BuildServiceProvider();
    }

    private static SqlDocumentViewModel CreateSqlDocumentViewModel(string documentId)
    {
        OfflineTabData offlineTabData = new()
        {
            MyId = documentId,
            Title = "Test document",
            SqlText = null,
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
            Title = "Test document"
        };
    }
}
