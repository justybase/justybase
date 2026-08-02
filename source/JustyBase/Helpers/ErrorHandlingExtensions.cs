using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Helpers;

internal static class ErrorHandlingExtensions
{
    public static void LogAndShowError(this ISimpleLogger logger, Exception ex, IMessageForUserTools messageForUserTools, bool isCrash = false)
    {
        logger.TrackError(ex, isCrash: isCrash);
        messageForUserTools.ShowSimpleMessageBoxInstance(ex);
    }

    public static void LogAndShowError(this ISimpleLogger logger, Exception ex, IMessageForUserTools messageForUserTools, string message, string title = "Error", bool isCrash = false)
    {
        logger.TrackError(ex, isCrash: isCrash);
        messageForUserTools.ShowSimpleMessageBoxInstance(message, title);
    }

    public static string ExecuteWithErrorHandling(
        this ISimpleLogger logger,
        Func<string> operation,
        string errorPrefix)
    {
        try
        {
            return operation();
        }
        catch (OperationCanceledException)
        {
            return $"{errorPrefix}: Operation was cancelled.";
        }
        catch (Exception ex)
        {
            logger.TrackError(ex, isCrash: false);
            return $"{errorPrefix}: {ex.Message}";
        }
    }

    public static async Task<string> ExecuteWithErrorHandlingAsync(
        this ISimpleLogger logger,
        Func<Task<string>> operation,
        string errorPrefix)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            return $"{errorPrefix}: Operation was cancelled.";
        }
        catch (Exception ex)
        {
            logger.TrackError(ex, isCrash: false);
            return $"{errorPrefix}: {ex.Message}";
        }
    }
}
