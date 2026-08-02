using System.Text;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Logging;

/// <summary>
/// File-backed <see cref="ISimpleLogger"/> with size-based rotation and credential redaction.
/// Writes to <c>errors.log</c> only when file logging is enabled; crashes may also get a <c>crash-*.txt</c> snapshot.
/// </summary>
public sealed class FileSimpleLogger : ISimpleLogger, IDisposable
{
    private const long MaxLogFileBytes = 5 * 1024 * 1024;
    private const int MaxRotatedFiles = 5;
    private const string LogFileName = "errors.log";

    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly bool _openMessagesInNotepad;
    private readonly Func<bool> _isEnabled;
    private bool _disposed;

    public FileSimpleLogger()
        : this(IGeneralApplicationData.LogsPath, openMessagesInNotepad: true, isEnabled: () => false)
    {
    }

    /// <summary>
    /// Creates a logger that writes under <paramref name="logDirectory"/> (used by tests).
    /// </summary>
    public FileSimpleLogger(string logDirectory, bool openMessagesInNotepad = true, Func<bool>? isEnabled = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _logDirectory = logDirectory;
        _logFilePath = Path.Combine(_logDirectory, LogFileName);
        _openMessagesInNotepad = openMessagesInNotepad;
        // Tests omit the gate and expect writes; production DI always passes Config.EnableFileLogging.
        _isEnabled = isEnabled ?? (() => true);
        Directory.CreateDirectory(_logDirectory);
    }

    public string LogFilePath => _logFilePath;

    public string LogDirectory => _logDirectory;

    public void Dispose()
    {
        _disposed = true;
    }

    public Task TrackCrashAsync(Exception ex, bool isCrash)
    {
        WriteEntry("CRASH", FormatException(ex), isCrash, openNotepad: false);
        return Task.CompletedTask;
    }

    public void TrackError(Exception ex, bool isCrash)
    {
        WriteEntry("ERROR", FormatException(ex), isCrash, openNotepad: false);
    }

    public void TrackCrashMessagePlusOpenNotepad(string message, string type, bool isCrash)
    {
        var body =
            $"""
            isCrash : {isCrash}
            type : {type}
            message : {message}
            """;
        WriteEntry("CRASH", body, isCrash, openNotepad: true);
    }

    public void TrackCrashMessagePlusOpenNotepad(Exception ex, string type, bool isCrash)
    {
        TrackCrashMessagePlusOpenNotepad(
            $"""
            MESSAGE
                {ex.Message}
            STACKTRACE
                {ex}
            """, type, isCrash);
    }

    public void OpenMessageInNotepad(string message)
    {
        if (!_openMessagesInNotepad || !IsFileLoggingEnabled())
        {
            return;
        }

        try
        {
            // Prefer opening a durable file under LogsPath rather than a throwaway temp path.
            var snapshotPath = WriteCrashSnapshot(LogMessageRedactor.Redact(message));
            var pathToOpen = snapshotPath ?? _logFilePath;
            OpenExistingLogInNotepad(pathToOpen);
        }
        catch (Exception)
        {
        }
    }

    private bool IsFileLoggingEnabled()
    {
        try
        {
            return _isEnabled();
        }
        catch
        {
            return false;
        }
    }

    private void WriteEntry(string level, string body, bool isCrash, bool openNotepad)
    {
        if (_disposed || !IsFileLoggingEnabled())
        {
            return;
        }

        try
        {
            var redacted = LogMessageRedactor.Redact(body);
            var sb = new StringBuilder(redacted.Length + 96);
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
              .Append(level)
              .Append(" isCrash=").Append(isCrash)
              .AppendLine()
              .AppendLine(redacted)
              .AppendLine("---");

            string? snapshotPath = null;
            lock (_sync)
            {
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, sb.ToString());
                if (isCrash)
                {
                    snapshotPath = WriteCrashSnapshotUnlocked(redacted);
                }
            }

            if (openNotepad && _openMessagesInNotepad)
            {
                OpenExistingLogInNotepad(snapshotPath ?? _logFilePath);
            }
        }
        catch (Exception)
        {
            // Logging must never throw into app flows.
        }
    }

    private string? WriteCrashSnapshot(string redactedBody)
    {
        lock (_sync)
        {
            return WriteCrashSnapshotUnlocked(redactedBody);
        }
    }

    private string? WriteCrashSnapshotUnlocked(string redactedBody)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(
                _logDirectory,
                $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            var header =
                $"""
                JustyBase crash snapshot
                Written: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
                Log directory: {_logDirectory}
                Rolling log: {_logFilePath}

                """;
            File.WriteAllText(path, header + redactedBody + Environment.NewLine);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void OpenExistingLogInNotepad(string pathToOpen)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(pathToOpen))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{pathToOpen}\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var info = new FileInfo(_logFilePath);
            if (info.Length < MaxLogFileBytes)
            {
                return;
            }

            var oldest = Path.Combine(_logDirectory, $"{LogFileName}.{MaxRotatedFiles}");
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (int i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(_logDirectory, $"{LogFileName}.{i}");
                var dst = Path.Combine(_logDirectory, $"{LogFileName}.{i + 1}");
                if (File.Exists(src))
                {
                    File.Move(src, dst);
                }
            }

            File.Move(_logFilePath, Path.Combine(_logDirectory, $"{LogFileName}.1"));
        }
        catch (Exception)
        {
            // Best-effort rotation; append may still succeed.
        }
    }

    private static string FormatException(Exception ex)
    {
        return
            $"""
            {ex.GetType().FullName}: {ex.Message}
            {ex}
            """;
    }
}
