using JustyBase.Public.Lib.Services;

namespace JustyBase.Services;

public interface IMainWindowActivationService
{
    bool TryOpenStartupSqlFile(string[] args, Action<string> openSqlFile);

    PipeCommunicationService CreatePipeCommunicationService(
        string pipeName,
        Action<string> activateOpenedFileAction,
        Action restoreAction,
        Action<Exception> exceptionAction);

    void RestoreMainWindow(IAvaloniaSpecificHelpers avaloniaSpecificHelpers);
}

public sealed class MainWindowActivationService : IMainWindowActivationService
{
    public bool TryOpenStartupSqlFile(string[] args, Action<string> openSqlFile)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(openSqlFile);

        if (!TryGetStartupSqlPath(args, out string startupSqlFilePath))
        {
            return false;
        }

        openSqlFile(startupSqlFilePath);
        return true;
    }

    public PipeCommunicationService CreatePipeCommunicationService(
        string pipeName,
        Action<string> activateOpenedFileAction,
        Action restoreAction,
        Action<Exception> exceptionAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(activateOpenedFileAction);
        ArgumentNullException.ThrowIfNull(restoreAction);
        ArgumentNullException.ThrowIfNull(exceptionAction);

        return new PipeCommunicationService(pipeName)
        {
            ActivateOpenedFileAction = activateOpenedFileAction,
            RestoreAction = restoreAction,
            ExceptionAction = exceptionAction
        };
    }

    public void RestoreMainWindow(IAvaloniaSpecificHelpers avaloniaSpecificHelpers)
    {
        ArgumentNullException.ThrowIfNull(avaloniaSpecificHelpers);

        if (avaloniaSpecificHelpers.GetMainWindow() is not Window mainWindow)
        {
            return;
        }

        if (mainWindow.WindowState != WindowState.Maximized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }
    }

    private static bool TryGetStartupSqlPath(string[] args, out string startupSqlFilePath)
    {
        startupSqlFilePath = string.Empty;
        if (args.Length <= 1)
        {
            return false;
        }

        string lastArgument = args[^1];
        if (!lastArgument.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        startupSqlFilePath = lastArgument;
        return true;
    }
}
