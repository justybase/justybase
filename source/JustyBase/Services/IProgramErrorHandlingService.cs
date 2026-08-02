using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Logging;
using System.Text;

namespace JustyBase.Services;

public interface IProgramErrorHandlingService
{
    void HandleStartupException(Exception exception, ISimpleLogger? simpleLogger, IMessageForUserTools? messageForUserTools);

    void HandleUiThreadException(Exception exception, ISimpleLogger? simpleLogger, IMessageForUserTools? messageForUserTools, string source);

    void HandleCurrentDomainUnhandledException(object? exceptionObject, string fallbackEventText, ISimpleLogger? simpleLogger);

    void HandleUnobservedTaskException(
        AggregateException exception,
        IGeneralApplicationData? generalApplicationData,
        ISimpleLogger? simpleLogger,
        IMessageForUserTools? messageForUserTools);

    string BuildUnobservedTaskExceptionMessage(AggregateException exception);

    bool ShouldIgnoreUnobservedTaskException(string message);
}

public sealed class ProgramErrorHandlingService : IProgramErrorHandlingService
{
    private static readonly HashSet<string> IgnoredErrorMessages =
    [
        "Unobserved Task Exception Message: \r\n    A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (Operacja We/Wy została przerwana z powodu zakończenia wątku lub żądania aplikacji.)\r\nUnobserved Task Exception StackTrace\r\n    \r\nUnobserved Task Exception Source\r\n    \r\n##### InnerExceptions start\r\nUnobserved Task Exception Message\r\nOperacja We/Wy została przerwana z powodu zakończenia wątku lub żądania aplikacji.\r\nUnobserved Task Exception StackTrace\r\n   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)\r\n   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)\r\n   at System.Threading.Tasks.ValueTask.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)\r\nUnobserved Task Exception Source\r\nSystem.Net.Sockets\r\n##### InnerExceptions end\r\n"
    ];

    public void HandleStartupException(Exception exception, ISimpleLogger? simpleLogger, IMessageForUserTools? messageForUserTools)
    {
        var logger = ResolveDiskLogger(simpleLogger);
        logger.TrackCrashMessagePlusOpenNotepad(exception, "Global try_catch", true);

        if (exception.InnerException is Exception innerException)
        {
            logger.TrackCrashMessagePlusOpenNotepad(innerException, "Global try_catch_inner", true);
            logger.TrackCrashAsync(innerException, true).Wait(TimeSpan.FromSeconds(5));

            if (innerException is System.Security.Cryptography.CryptographicException)
            {
                messageForUserTools?.ShowSimpleMessageBoxInstance(innerException);
            }
        }

        logger.TrackCrashAsync(exception, true).Wait(TimeSpan.FromSeconds(5));
    }

    public void HandleUiThreadException(Exception exception, ISimpleLogger? simpleLogger, IMessageForUserTools? messageForUserTools, string source)
    {
        var logger = ResolveDiskLogger(simpleLogger);
        // Persist full exception (type + stack), not only Message.
        logger.TrackCrashMessagePlusOpenNotepad(exception, source, isCrash: true);
        messageForUserTools?.ShowSimpleMessageBoxInstance(exception);
    }

    public void HandleCurrentDomainUnhandledException(object? exceptionObject, string fallbackEventText, ISimpleLogger? simpleLogger)
    {
        var logger = ResolveDiskLogger(simpleLogger);
        logger.TrackCrashMessagePlusOpenNotepad(exceptionObject?.ToString() ?? "empty message", "CurrentDomain_UnhandledException_1", isCrash: true);

        if (exceptionObject is Exception exception)
        {
            logger.TrackCrashMessagePlusOpenNotepad(exception, "CurrentDomain_UnhandledException_2", true);
        }

        if (exceptionObject is TypeInitializationException typeInitializationException && typeInitializationException.InnerException is not null)
        {
            logger.TrackCrashMessagePlusOpenNotepad(typeInitializationException, "CurrentDomain_UnhandledException_3", true);
            logger.TrackCrashAsync(typeInitializationException, true).Wait(TimeSpan.FromSeconds(5));
        }

        logger.TrackCrashAsync(new Exception(fallbackEventText), true).Wait(TimeSpan.FromSeconds(5));
    }

    public void HandleUnobservedTaskException(
        AggregateException exception,
        IGeneralApplicationData? generalApplicationData,
        ISimpleLogger? simpleLogger,
        IMessageForUserTools? messageForUserTools)
    {
        generalApplicationData?.SaveConfig();

        string message = BuildUnobservedTaskExceptionMessage(exception);
        if (ShouldIgnoreUnobservedTaskException(message))
        {
            return;
        }

        var logger = ResolveDiskLogger(simpleLogger);
        logger.TrackCrashMessagePlusOpenNotepad(message, "TaskScheduler_UnobservedTaskException UnobservedTaskException", isCrash: true);
        messageForUserTools?.ShowSimpleMessageBoxInstance(message, "Error");
    }

    public string BuildUnobservedTaskExceptionMessage(AggregateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        StringBuilder sb = new();
        sb.AppendLine($"""
                Unobserved Task Exception Message: 
                    {exception.Message}
                Unobserved Task Exception StackTrace
                    {exception.StackTrace}
                Unobserved Task Exception Source
                    {exception.Source}
                ##### InnerExceptions start
                """);

        foreach (Exception innerException in exception.InnerExceptions)
        {
            sb.AppendLine("Unobserved Task Exception Message");
            sb.AppendLine(innerException.Message);

            sb.AppendLine("Unobserved Task Exception StackTrace");
            sb.AppendLine(innerException.StackTrace);

            sb.AppendLine("Unobserved Task Exception Source");
            sb.AppendLine(innerException.Source);
        }

        sb.AppendLine("##### InnerExceptions end");
        return sb.ToString();
    }

    public bool ShouldIgnoreUnobservedTaskException(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Contains("com.canonical.AppMenu.Registrar", StringComparison.Ordinal))
        {
            return true;
        }
        if (IgnoredErrorMessages.Contains(message))
        {
            return true;
        }
        if (message.Contains("Socket.AwaitableSocketAsyncEventArgs.ThrowException", StringComparison.Ordinal)
            && message.Contains("Operacja We/Wy", StringComparison.Ordinal))
        {
            return true;
        }
        if (message.Contains("System.OperationCanceledException", StringComparison.Ordinal)
            && message.Contains("ThrowIfCancellationRequested", StringComparison.Ordinal))
        {
            return true;
        }
        if (message.Contains("TaskCanceledException", StringComparison.Ordinal)
            && message.Contains("HttpClient", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Prefer the DI logger when present; otherwise create a file logger only if
    /// <c>EnableFileLogging</c> is on. Never falls back to always-on disk writes.
    /// </summary>
    internal static ISimpleLogger ResolveDiskLogger(ISimpleLogger? simpleLogger)
    {
        if (simpleLogger is FileSimpleLogger)
        {
            return simpleLogger;
        }

        if (simpleLogger is not null and not EmptyLogger)
        {
            return simpleLogger;
        }

        if (!IsFileLoggingEnabled())
        {
            return simpleLogger ?? ISimpleLogger.EmptyLogger;
        }

        try
        {
            return new FileSimpleLogger(
                IGeneralApplicationData.LogsPath,
                openMessagesInNotepad: true,
                isEnabled: () => true);
        }
        catch
        {
            return simpleLogger ?? ISimpleLogger.EmptyLogger;
        }
    }

    private static bool IsFileLoggingEnabled()
    {
        try
        {
            var config = Program.ServiceProvider?.GetService(typeof(IGeneralApplicationData)) as IGeneralApplicationData;
            return config?.Config.EnableFileLogging == true;
        }
        catch
        {
            return false;
        }
    }
}
