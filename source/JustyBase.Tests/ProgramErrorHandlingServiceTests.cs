using JustyBase.Common.Contracts;
using JustyBase.Services;
using Moq;
using System.Security.Cryptography;

namespace JustyBase.Tests;

public sealed class ProgramErrorHandlingServiceTests
{
    [Fact]
    public void BuildUnobservedTaskExceptionMessage_IncludesInnerExceptions()
    {
        var exception = new AggregateException(
            "outer",
            new InvalidOperationException("inner-one"),
            new ArgumentException("inner-two"));
        var sut = new ProgramErrorHandlingService();

        string message = sut.BuildUnobservedTaskExceptionMessage(exception);

        Assert.Contains("outer", message);
        Assert.Contains("inner-one", message);
        Assert.Contains("inner-two", message);
        Assert.Contains("##### InnerExceptions end", message);
    }

    [Fact]
    public void ShouldIgnoreUnobservedTaskException_WhenRegistrarNoiseIsPresent_ReturnsTrue()
    {
        var sut = new ProgramErrorHandlingService();

        bool result = sut.ShouldIgnoreUnobservedTaskException("error from com.canonical.AppMenu.Registrar bridge");

        Assert.True(result);
    }

    [Fact]
    public void HandleStartupException_WhenInnerExceptionIsCryptographic_LogsAndShowsMessage()
    {
        var innerException = new CryptographicException("crypto");
        var exception = new InvalidOperationException("outer", innerException);
        var simpleLogger = new Mock<JustyBase.PluginCommon.Contracts.ISimpleLogger>();
        simpleLogger.Setup(x => x.TrackCrashAsync(It.IsAny<Exception>(), true)).Returns(Task.CompletedTask);
        var messageForUserTools = new Mock<IMessageForUserTools>();
        var sut = new ProgramErrorHandlingService();

        sut.HandleStartupException(exception, simpleLogger.Object, messageForUserTools.Object);

        simpleLogger.Verify(x => x.TrackCrashMessagePlusOpenNotepad(exception, "Global try_catch", true), Times.Once);
        simpleLogger.Verify(x => x.TrackCrashMessagePlusOpenNotepad(innerException, "Global try_catch_inner", true), Times.Once);
        simpleLogger.Verify(x => x.TrackCrashAsync(innerException, true), Times.Once);
        simpleLogger.Verify(x => x.TrackCrashAsync(exception, true), Times.Once);
        messageForUserTools.Verify(x => x.ShowSimpleMessageBoxInstance(innerException), Times.Once);
    }

    [Fact]
    public void HandleUiThreadException_LogsAndShowsMessage()
    {
        var exception = new InvalidOperationException("boom");
        var simpleLogger = new Mock<JustyBase.PluginCommon.Contracts.ISimpleLogger>();
        var messageForUserTools = new Mock<IMessageForUserTools>();
        var sut = new ProgramErrorHandlingService();

        sut.HandleUiThreadException(exception, simpleLogger.Object, messageForUserTools.Object, "UIThread");

        simpleLogger.Verify(x => x.TrackCrashMessagePlusOpenNotepad(exception, "UIThread", true), Times.Once);
        messageForUserTools.Verify(x => x.ShowSimpleMessageBoxInstance(exception), Times.Once);
    }

    [Fact]
    public void ResolveDiskLogger_WhenNullAndFileLoggingDisabled_ReturnsEmptyLogger()
    {
        var logger = ProgramErrorHandlingService.ResolveDiskLogger(null);

        Assert.Same(JustyBase.PluginCommon.Contracts.ISimpleLogger.EmptyLogger, logger);
    }

    [Fact]
    public void ResolveDiskLogger_WhenFileSimpleLoggerProvided_ReturnsSameInstance()
    {
        using var fileLogger = new JustyBase.Services.Logging.FileSimpleLogger(
            Path.Combine(Path.GetTempPath(), "JustyBase_ResolveDisk_" + Guid.NewGuid().ToString("N")),
            openMessagesInNotepad: false);

        var logger = ProgramErrorHandlingService.ResolveDiskLogger(fileLogger);

        Assert.Same(fileLogger, logger);
    }
}
