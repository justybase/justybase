using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.Documents;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlDocumentInteractionServicesTests
{
    [Fact]
    public async Task GetClipboardTextAsync_DelegatesToClipboardService()
    {
        var clipboardService = new Mock<IClipboardService>();
        clipboardService.Setup(service => service.GetTextAsync()).ReturnsAsync("value");
        using var interactionServices = CreateService(clipboardService: clipboardService);

        string result = await interactionServices.GetClipboardTextAsync();

        Assert.Equal("value", result);
        clipboardService.Verify(service => service.GetTextAsync(), Times.Once);
    }

    [Fact]
    public void BuildPasteAsIn_DelegatesToExportOperations()
    {
        var exportOperations = new Mock<ISqlExportOperations>();
        exportOperations.Setup(service => service.BuildPasteAsIn("Text", "1\t2"))
            .Returns("IN ('1','2')");
        using var interactionServices = CreateService(exportOperations: exportOperations);

        string result = interactionServices.BuildPasteAsIn("Text", "1\t2");

        Assert.Equal("IN ('1','2')", result);
        exportOperations.Verify(service => service.BuildPasteAsIn("Text", "1\t2"), Times.Once);
    }

    [Fact]
    public async Task ImportFromClipboardAsync_ForwardsInjectedClipboardService()
    {
        var clipboardService = new Mock<IClipboardService>();
        var importService = new Mock<ISqlImportService>();
        var clipboard = new Mock<IClipboard>().Object;
        var appData = new Mock<IGeneralApplicationData>().Object;
        using var interactionServices = CreateService(
            clipboardService: clipboardService,
            importService: importService);

        await interactionServices.ImportFromClipboardAsync(
            clipboard,
            appData,
            "main",
            "db",
            static (_, _, _, _) => null,
            static (_, _) => { });

        importService.Verify(service => service.ImportFromClipboardAsync(
            clipboardService.Object,
            clipboard,
            appData,
            "main",
            "db",
            It.IsAny<Func<string, LogMessageType, DateTime, string, LogMessage?>>(),
            It.IsAny<Action<object, bool>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteObjectActionAsync_DelegatesToDbObjectActionService()
    {
        var actionService = new Mock<IDbObjectActionService>();
        var expected = new DbObjectActionResult { TextToInsert = "SELECT * FROM test" };
        actionService.Setup(service => service.ExecuteObjectActionAsync("Select", "test", "main", "db", null))
            .ReturnsAsync(expected);
        using var interactionServices = CreateService(dbObjectActionService: actionService);

        var result = await interactionServices.ExecuteObjectActionAsync("Select", "test", "main", "db", null);

        Assert.Same(expected, result);
        actionService.Verify(service => service.ExecuteObjectActionAsync("Select", "test", "main", "db", null), Times.Once);
    }

    [Fact]
    public void Dispose_DisposesFileWatcherService()
    {
        var fileWatcherService = new Mock<ISqlFileWatcherService>();
        var interactionServices = CreateService(fileWatcherService: fileWatcherService);

        interactionServices.Dispose();

        fileWatcherService.Verify(service => service.Dispose(), Times.Once);
    }

    private static SqlDocumentInteractionServices CreateService(
        Mock<IClipboardService>? clipboardService = null,
        Mock<ISqlImportService>? importService = null,
        Mock<ISqlFileWatcherService>? fileWatcherService = null,
        Mock<ISqlExportOperations>? exportOperations = null,
        Mock<IDbObjectActionService>? dbObjectActionService = null)
    {
        return new SqlDocumentInteractionServices(
            (clipboardService ?? new Mock<IClipboardService>()).Object,
            (importService ?? new Mock<ISqlImportService>()).Object,
            (fileWatcherService ?? new Mock<ISqlFileWatcherService>()).Object,
            (exportOperations ?? new Mock<ISqlExportOperations>()).Object,
            (dbObjectActionService ?? new Mock<IDbObjectActionService>()).Object);
    }
}
