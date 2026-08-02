using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.Documents;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlDocumentUiServicesTests
{
    [Fact]
    public void GetClipboard_ReturnsClipboardFromHelpers()
    {
        var clipboard = new Mock<IClipboard>().Object;
        var helpers = new Mock<IAvaloniaSpecificHelpers>();
        helpers.Setup(service => service.GetClipboard()).Returns(clipboard);
        var service = CreateService(helpers: helpers);

        var result = service.GetClipboard();

        Assert.Same(clipboard, result);
    }

    [Fact]
    public async Task PickOpenSqlFilePathAsync_WhenStorageProviderIsMissing_ReturnsNull()
    {
        var helpers = new Mock<IAvaloniaSpecificHelpers>();
        helpers.Setup(service => service.GetStorageProvider()).Returns((Avalonia.Platform.Storage.IStorageProvider?)null);
        var service = CreateService(helpers: helpers);

        var result = await service.PickOpenSqlFilePathAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task PickSavePathAsync_WhenStorageProviderIsMissing_ReturnsNull()
    {
        var helpers = new Mock<IAvaloniaSpecificHelpers>();
        helpers.Setup(service => service.GetStorageProvider()).Returns((Avalonia.Platform.Storage.IStorageProvider?)null);
        var service = CreateService(helpers: helpers);

        var result = await service.PickSavePathAsync("csv files", "*.csv", "csv");

        Assert.Null(result);
    }

    [Fact]
    public async Task CopySelectionWithFormatsAsync_WhenEditorIsNull_Completes()
    {
        var service = CreateService();

        await service.CopySelectionWithFormatsAsync(editor: null);
    }

    [Fact]
    public void FocusEditorOnSelectedTab_WhenEditorIsNull_DoesNotThrow()
    {
        var service = CreateService();

        service.FocusEditorOnSelectedTab(editor: null);
    }

    private static SqlDocumentUiServices CreateService(
        Mock<IAvaloniaSpecificHelpers>? helpers = null,
        Mock<IDocumentFontService>? fontService = null,
        Mock<IMessageForUserTools>? messageForUserTools = null,
        Mock<ISimpleLogger>? simpleLogger = null)
    {
        return new SqlDocumentUiServices(
            (helpers ?? new Mock<IAvaloniaSpecificHelpers>()).Object,
            (fontService ?? new Mock<IDocumentFontService>()).Object,
            (messageForUserTools ?? new Mock<IMessageForUserTools>()).Object,
            (simpleLogger ?? new Mock<ISimpleLogger>()).Object);
    }
}
