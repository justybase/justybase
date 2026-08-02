using JustyBase.Public.Lib.Services;
using JustyBase.Services;
using Moq;

namespace JustyBase.Tests;

public sealed class MainWindowActivationServiceTests
{
    [Fact]
    public void TryOpenStartupSqlFile_WhenLastArgumentIsSql_OpensFileAndReturnsTrue()
    {
        var sut = new MainWindowActivationService();
        string? openedPath = null;

        bool result = sut.TryOpenStartupSqlFile(
            ["JustyBase.exe", @"C:\tmp\sample.sql"],
            path => openedPath = path);

        Assert.True(result);
        Assert.Equal(@"C:\tmp\sample.sql", openedPath);
    }

    [Fact]
    public void TryOpenStartupSqlFile_WhenNoSqlArgumentExists_ReturnsFalse()
    {
        var sut = new MainWindowActivationService();
        bool wasCalled = false;

        bool result = sut.TryOpenStartupSqlFile(
            ["JustyBase.exe", @"C:\tmp\sample.txt"],
            _ => wasCalled = true);

        Assert.False(result);
        Assert.False(wasCalled);
    }

    [Fact]
    public void CreatePipeCommunicationService_WiresProvidedCallbacks()
    {
        var sut = new MainWindowActivationService();
        string? openedFile = null;
        bool restoreCalled = false;
        Exception? capturedException = null;

        PipeCommunicationService result = sut.CreatePipeCommunicationService(
            "JUST_X",
            path => openedFile = path,
            () => restoreCalled = true,
            exception => capturedException = exception);

        result.ActivateOpenedFileAction?.Invoke("demo.sql");
        result.RestoreAction?.Invoke();
        result.ExceptionAction(new InvalidOperationException("boom"));

        Assert.Equal("demo.sql", openedFile);
        Assert.True(restoreCalled);
        Assert.Equal("boom", capturedException?.Message);
    }

    [Fact]
    public void RestoreMainWindow_WhenMainWindowIsUnavailable_DoesNotThrow()
    {
        var helpers = new Mock<IAvaloniaSpecificHelpers>();
        helpers.Setup(x => x.GetMainWindow()).Returns((Avalonia.Controls.Window?)null);

        var sut = new MainWindowActivationService();

        var exception = Record.Exception(() => sut.RestoreMainWindow(helpers.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void RestoreMainWindow_WhenHelpersAreMissing_ThrowsArgumentNullException()
    {
        var sut = new MainWindowActivationService();

        Assert.Throws<ArgumentNullException>(() => sut.RestoreMainWindow(null!));
    }
}
