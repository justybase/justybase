using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services.Documents;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlRunPreparationServiceTests
{
    [Fact]
    public void ValidateRunStart_WhenEditorIsMissing_ReturnsMissingEditorStatus()
    {
        var service = CreateService();

        var result = service.ValidateRunStart(hasSqlEditor: false, selectedConnectionIndex: 0);

        Assert.False(result.CanRun);
        Assert.Equal(SqlRunStartValidationStatus.MissingEditor, result.Status);
        Assert.Null(result.MessageForUser);
    }

    [Fact]
    public void ValidateRunStart_WhenConnectionIsMissing_ReturnsMessageForUser()
    {
        var service = CreateService();

        var result = service.ValidateRunStart(hasSqlEditor: true, selectedConnectionIndex: -1);

        Assert.False(result.CanRun);
        Assert.Equal(SqlRunStartValidationStatus.MissingConnection, result.Status);
        Assert.Equal("please select connection", result.MessageForUser);
    }

    [Fact]
    public void CreateExecutionSettings_WhenExportOptionIsUsed_FlagsExportPathSelection()
    {
        var service = CreateService();

        var settings = service.CreateExecutionSettings(
            keepConnectionOpen: true,
            doPooling: false,
            localTitle: "Run 1",
            option: ".csv");

        Assert.Equal("Run 1", settings.LocalTitle);
        Assert.True(settings.KeepConnectionOpen);
        Assert.False(settings.DoPooling);
        Assert.True(settings.RequiresExportPathSelection);
        Assert.True(settings.ShouldDisableRun);
    }

    [Fact]
    public void PrepareQuery_WhenQueryIsTooShort_ReturnsNoExecutionPlan()
    {
        var service = CreateService();

        var result = service.PrepareQuery("abc", currentSqlPositionInEditor: 14, option: "Grid", singleCommand: false, continueOnError: false);

        Assert.False(result.HasQuery);
        Assert.Equal("abc", result.Query);
        Assert.Equal(14, result.CurrentSqlPositionInEditor);
        Assert.False(result.HasSessionVariableDefinition);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public void PrepareQuery_WhenSessionVariableDeclarationIsUsed_DetectsVariableDefinition()
    {
        var service = CreateService();

        var result = service.PrepareQuery(
            "declare &test_value = 1",
            currentSqlPositionInEditor: 3,
            option: "Grid",
            singleCommand: false,
            continueOnError: false);

        Assert.True(result.HasQuery);
        Assert.True(result.HasSessionVariableDefinition);
        Assert.NotNull(result.ExecutionPlan);
        Assert.Equal("&test_value", result.VariableDefineMatch.Groups["sessionVar"].Value);
    }

    [Fact]
    public void PrepareQuery_WhenContinueOnErrorMarkerExists_UpdatesExecutionPlan()
    {
        var service = CreateService();
        string query = $"select 1{Environment.NewLine}{DatabaseService.CONTINUE_ON_ERROR}";

        var result = service.PrepareQuery(
            query,
            currentSqlPositionInEditor: 9,
            option: ".xlsx",
            singleCommand: false,
            continueOnError: false);

        Assert.True(result.HasQuery);
        Assert.NotNull(result.ExecutionPlan);
        Assert.True(result.ExecutionPlan!.ContinueOnError);
        Assert.True(result.ExecutionPlan.SingleCommand);
    }

    [Fact]
    public async Task InitializeDatabaseServiceAsync_WhenDriverIsMissing_LoadsPluginsBeforeResolvingService()
    {
        var appData = new Mock<IGeneralApplicationData>();
        var resolver = new Mock<IDatabaseServiceResolver>();
        var dbService = new Mock<IDatabaseService>().Object;
        bool pluginsLoaded = false;

        resolver.Setup(r => r.IsDriverRegistered(appData.Object, "main")).Returns(false);
        resolver.Setup(r => r.GetDatabaseService(
                appData.Object,
                "main",
                false,
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()))
            .Returns(dbService);

        var service = CreateService(appData, resolver);

        var result = await service.InitializeDatabaseServiceAsync(
            "main",
            () =>
            {
                pluginsLoaded = true;
                return Task.CompletedTask;
            });

        Assert.True(pluginsLoaded);
        Assert.Same(dbService, result);
        resolver.Verify(r => r.IsDriverRegistered(appData.Object, "main"), Times.Once);
        resolver.Verify(r => r.GetDatabaseService(
            appData.Object,
            "main",
            false,
            It.IsAny<bool>(),
            It.IsAny<Action<string>?>()), Times.Once);
    }

    [Fact]
    public async Task InitializeDatabaseServiceAsync_WhenDriverIsAlreadyRegistered_SkipsPluginLoading()
    {
        var appData = new Mock<IGeneralApplicationData>();
        var resolver = new Mock<IDatabaseServiceResolver>();
        var dbService = new Mock<IDatabaseService>().Object;
        bool pluginsLoaded = false;

        resolver.Setup(r => r.IsDriverRegistered(appData.Object, "main")).Returns(true);
        resolver.Setup(r => r.GetDatabaseService(
                appData.Object,
                "main",
                false,
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()))
            .Returns(dbService);

        var service = CreateService(appData, resolver);

        var result = await service.InitializeDatabaseServiceAsync(
            "main",
            () =>
            {
                pluginsLoaded = true;
                return Task.CompletedTask;
            });

        Assert.False(pluginsLoaded);
        Assert.Same(dbService, result);
        resolver.Verify(r => r.IsDriverRegistered(appData.Object, "main"), Times.Once);
        resolver.Verify(r => r.GetDatabaseService(
            appData.Object,
            "main",
            false,
            It.IsAny<bool>(),
            It.IsAny<Action<string>?>()), Times.Once);
    }

    private static SqlRunPreparationService CreateService(
        Mock<IGeneralApplicationData>? appData = null,
        Mock<IDatabaseServiceResolver>? resolver = null)
    {
        return new SqlRunPreparationService(
            (appData ?? new Mock<IGeneralApplicationData>()).Object,
            (resolver ?? new Mock<IDatabaseServiceResolver>()).Object);
    }
}
