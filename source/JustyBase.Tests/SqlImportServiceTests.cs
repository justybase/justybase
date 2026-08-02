using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.Documents;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlImportServiceTests
{
    private readonly Mock<IDatabaseServiceResolver> _resolver = new();
    private readonly Mock<ISimpleLogger> _logger = new();
    private readonly Mock<IMessageForUserTools> _messages = new();
    private readonly Mock<IGeneralApplicationData> _appData = new();
    private readonly SqlImportService _sut;

    public SqlImportServiceTests()
    {
        _sut = new SqlImportService(_resolver.Object, _logger.Object, _messages.Object);
    }

    [Fact]
    public async Task ImportFromFilePathAsync_UnsupportedExtension_InsertsNotImported()
    {
        string? inserted = null;
        bool? append = null;

        await _sut.ImportFromFilePathAsync(
            @"C:\temp\notes.txt",
            _appData.Object,
            "conn",
            static (_, _, _, _) => null,
            (text, isAppend) =>
            {
                inserted = text?.ToString();
                append = isAppend;
            });

        Assert.Equal("\nnot imported", inserted);
        Assert.True(append);
        _resolver.Verify(
            r => r.GetDatabaseService(
                It.IsAny<IGeneralApplicationData>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportFromFilePathAsync_EmptyConnectionName_ReturnsWithoutResolver()
    {
        await _sut.ImportFromFilePathAsync(
            @"C:\temp\data.csv",
            _appData.Object,
            connectionName: "",
            static (_, _, _, _) => null,
            static (_, _) => { });

        _resolver.Verify(
            r => r.GetDatabaseService(
                It.IsAny<IGeneralApplicationData>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportFromFilePathAsync_ResolverReturnsNull_ReturnsWithoutImport()
    {
        _resolver
            .Setup(r => r.GetDatabaseService(
                _appData.Object,
                "conn",
                true,
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()))
            .Returns((IDatabaseService?)null);

        await _sut.ImportFromFilePathAsync(
            @"C:\temp\data.csv",
            _appData.Object,
            "conn",
            static (_, _, _, _) => null,
            static (_, _) => { });

        _resolver.Verify(
            r => r.GetDatabaseService(
                _appData.Object,
                "conn",
                true,
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportFromClipboardAsync_UnsupportedFormats_IsNoOp()
    {
        var clipboardService = new Mock<IClipboardService>();
        clipboardService.Setup(c => c.GetFormatsAsync()).ReturnsAsync(["Image"]);
        var clipboard = new Mock<IClipboard>();

        await _sut.ImportFromClipboardAsync(
            clipboardService.Object,
            clipboard.Object,
            _appData.Object,
            "conn",
            selectedDatabase: null,
            static (_, _, _, _) => null,
            static (_, _) => { });

        _resolver.Verify(
            r => r.GetDatabaseService(
                It.IsAny<IGeneralApplicationData>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportFromClipboardAsync_TextFormat_ResolverNull_ReturnsEarly()
    {
        var clipboardService = new Mock<IClipboardService>();
        clipboardService.Setup(c => c.GetFormatsAsync()).ReturnsAsync(["Text"]);
        var clipboard = new Mock<IClipboard>();
        _resolver
            .Setup(r => r.GetDatabaseService(
                _appData.Object,
                "conn",
                false,
                It.IsAny<bool>(),
                It.IsAny<Action<string>?>()))
            .Returns((IDatabaseService?)null);

        var logs = new List<string>();

        await _sut.ImportFromClipboardAsync(
            clipboardService.Object,
            clipboard.Object,
            _appData.Object,
            "conn",
            selectedDatabase: null,
            (msg, _, _, _) =>
            {
                logs.Add(msg);
                return null;
            },
            static (_, _) => { });

        Assert.Contains("waiting for database service", logs);
        Assert.DoesNotContain(logs, m => m.StartsWith("imported to", StringComparison.Ordinal));
        clipboardService.Verify(c => c.GetTextAsync(), Times.Never);
    }
}
