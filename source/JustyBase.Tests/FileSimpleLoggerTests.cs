using JustyBase.Services.Logging;

namespace JustyBase.Tests;

public sealed class FileSimpleLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileSimpleLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JustyBase_FileSimpleLogger_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void TrackError_WritesRedactedPasswordToLogFile()
    {
        using var logger = new FileSimpleLogger(_tempDir, openMessagesInNotepad: false);

        logger.TrackError(new InvalidOperationException("login failed Password=SuperSecret!"), isCrash: false);

        var content = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("ERROR", content);
        Assert.Contains("Password=***", content);
        Assert.DoesNotContain("SuperSecret!", content);
    }

    [Fact]
    public void TrackError_WhenFileLoggingDisabled_DoesNotWrite()
    {
        using var logger = new FileSimpleLogger(_tempDir, openMessagesInNotepad: false, isEnabled: () => false);

        logger.TrackError(new InvalidOperationException("should not be logged"), isCrash: false);

        Assert.False(File.Exists(logger.LogFilePath));
    }

    [Fact]
    public async Task TrackCrashAsync_WritesEntry()
    {
        using var logger = new FileSimpleLogger(_tempDir, openMessagesInNotepad: false);

        await logger.TrackCrashAsync(new Exception("boom ConnectionString=Server=x;Password=y"), isCrash: true);

        var content = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("CRASH", content);
        Assert.Contains("isCrash=True", content);
        Assert.DoesNotContain("Password=y", content);
        Assert.Contains("ConnectionString=***", content);
    }

    [Fact]
    public void TrackCrashMessagePlusOpenNotepad_WritesToLogWithoutLeakingPassword()
    {
        using var logger = new FileSimpleLogger(_tempDir, openMessagesInNotepad: false);

        logger.TrackCrashMessagePlusOpenNotepad("pwd=leakme", "unit-test", isCrash: false);

        var content = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("type : unit-test", content);
        Assert.Contains("pwd=***", content);
        Assert.DoesNotContain("leakme", content);
    }

    [Fact]
    public void TrackCrash_WritesDedicatedCrashTxtSnapshot()
    {
        using var logger = new FileSimpleLogger(_tempDir, openMessagesInNotepad: false);

        logger.TrackCrashMessagePlusOpenNotepad(
            new InvalidOperationException("SelectedTabId empty"),
            "Global try_catch",
            isCrash: true);

        var snapshots = Directory.GetFiles(_tempDir, "crash-*.txt");
        Assert.NotEmpty(snapshots);
        var snapshot = File.ReadAllText(snapshots[0]);
        Assert.Contains("JustyBase crash snapshot", snapshot);
        Assert.Contains("SelectedTabId empty", snapshot);
        Assert.Contains("Global try_catch", snapshot);
        Assert.Contains("errors.log", File.ReadAllText(logger.LogFilePath) + logger.LogFilePath);
    }
}
