using JustyBase.Common.Contracts;
using System.Diagnostics;
using System.Globalization;

namespace JustyBase;

internal static class StartupTrace
{
    private static readonly object SyncRoot = new();
    private static readonly string TracePath = Path.Combine(IGeneralApplicationData.LogsPath, "startup-update.log");
    // This file is independent of the application's config and DI container.
    // It must still exist when startup fails before logging is initialized.
    private static readonly string FallbackTracePath = Path.Combine(Path.GetTempPath(), "JustyBase-startup-debug.log");

    public static void Write(string message)
    {
        string line = $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} pid={Environment.ProcessId} {message}{Environment.NewLine}";

        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TracePath)!);
                File.AppendAllText(TracePath, line);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"StartupTrace failed: {exception.Message}");
        }

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(FallbackTracePath, line);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"StartupTrace fallback failed: {exception.Message}");
        }
    }

    public static void WriteException(string stage, Exception exception)
    {
        Write($"EXCEPTION stage={stage} type={exception.GetType().FullName} message={exception.Message}");
        Write($"EXCEPTION stage={stage} details={exception}");
    }
}
