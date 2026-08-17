using JustyBase.Common.Contracts;
using System.Diagnostics;
using System.Globalization;

namespace JustyBase;

internal static class StartupTrace
{
    private static readonly object SyncRoot = new();
    private static readonly string TracePath = Path.Combine(IGeneralApplicationData.LogsPath, "startup-update.log");

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
    }
}
