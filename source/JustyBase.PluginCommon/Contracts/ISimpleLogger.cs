namespace JustyBase.PluginCommon.Contracts;

public interface ISimpleLogger
{
    void Dispose();
    Task TrackCrashAsync(Exception ex, bool isCrash);
    void TrackCrashMessagePlusOpenNotepad(string message, string type, bool isCrash);
    void TrackError(Exception ex, bool isCrash);
    void TrackCrashMessagePlusOpenNotepad(Exception ex, string type, bool isCrash);
    void OpenMessageInNotepad(string message);

    static ISimpleLogger EmptyLogger { get; } = new EmptyLogger();
}

/// <summary>
/// No-op logger used before DI is ready or when file logging is disabled.
/// Does not write to disk (file logging is opt-in via <c>EnableFileLogging</c>).
/// </summary>
public sealed class EmptyLogger : ISimpleLogger
{
    public void Dispose()
    {
    }

    public void OpenMessageInNotepad(string message)
    {
    }

    public Task TrackCrashAsync(Exception ex, bool isCrash)
    {
        return Task.CompletedTask;
    }

    public void TrackCrashMessagePlusOpenNotepad(string message, string type, bool isCrash)
    {
    }

    public void TrackCrashMessagePlusOpenNotepad(Exception ex, string type, bool isCrash)
    {
    }

    public void TrackError(Exception ex, bool isCrash)
    {
    }
}
