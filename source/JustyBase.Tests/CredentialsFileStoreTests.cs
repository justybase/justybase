using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using System.Security.Cryptography;

namespace JustyBase.Tests;

/// <summary>
/// Regression tests for the "restart wipes saved connections" bug.
/// The old <c>SaveCredentials</c> did File.Delete-then-write (a crash left NO
/// file), seeded a fake SAMPLE over unreadable files, and had no .bak fallback.
/// All crash-safety now lives in <see cref="CredentialsFileStore"/> and is
/// covered here against a temp directory (never the real %AppData% location).
/// </summary>
public class CredentialsFileStoreTests
{
    private sealed class IdentityEncryptionHelper : IEncryptionHelper
    {
        public string Decrypt(string text) => text;
        public string Encrypt(string text) => text;
        public string GetEncodedContentOfTextFile(string realFilePath) => File.ReadAllText(realFilePath);
        public void SaveTextFileEncoded(string filePath, string fileContent) => File.WriteAllText(filePath, fileContent);
    }

    private sealed class ThrowingEncryptionHelper : IEncryptionHelper
    {
        private readonly IdentityEncryptionHelper _inner = new();
        private readonly bool _writePartialFirst;

        public ThrowingEncryptionHelper(bool writePartialFirst = false) => _writePartialFirst = writePartialFirst;

        public string Decrypt(string text) => _inner.Decrypt(text);
        public string Encrypt(string text) => _inner.Encrypt(text);
        public string GetEncodedContentOfTextFile(string realFilePath) => _inner.GetEncodedContentOfTextFile(realFilePath);

        public void SaveTextFileEncoded(string filePath, string fileContent)
        {
            if (_writePartialFirst)
            {
                // Simulate a crash mid-write: partial bytes hit the disk, then failure.
                File.WriteAllText(filePath, fileContent[..Math.Min(8, fileContent.Length)]);
            }

            throw new CryptographicException("Simulated encryption failure.");
        }
    }

    private static string NewTempDir()
    {
        string directory = Path.Combine(Path.GetTempPath(), "JustyBase.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDir(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static LoginDataModel Connection(string name) => new()
    {
        ConnectionName = name,
        Driver = "NetezzaSQL",
        Server = "db.example.com",
        Port = "5480",
        Database = "mydb",
        UserName = "user",
        Password = "secret",
    };

    [Fact]
    public void SaveThenLoad_RoundTripsConnections()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);

            Assert.True(store.TrySave([Connection("ONE"), Connection("TWO")], priorLoadFailed: false));

            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Loaded, new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger).Load(loaded));

            Assert.Equal(2, loaded.Count);
            Assert.Equal("db.example.com", loaded["ONE"].Server);
            Assert.Equal("secret", loaded["TWO"].Password);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Save_WithFailingEncryption_LeavesOriginalFileIntact()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var working = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            Assert.True(working.TrySave([Connection("SAVED")], priorLoadFailed: false));
            byte[] before = File.ReadAllBytes(path);

            // Old behavior called File.Delete BEFORE encrypting, so this throw
            // left no credentials file at all. The atomic write must not touch it.
            var broken = new CredentialsFileStore(path, new ThrowingEncryptionHelper(), ISimpleLogger.EmptyLogger);
            Assert.Throws<CryptographicException>(() => broken.TrySave([Connection("OTHER")], priorLoadFailed: false));

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));

            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Loaded, working.Load(loaded));
            Assert.Single(loaded);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Save_WithCrashMidWrite_LeavesOriginalFileIntact()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var working = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            Assert.True(working.TrySave([Connection("SAVED")], priorLoadFailed: false));
            byte[] before = File.ReadAllBytes(path);

            var crashing = new CredentialsFileStore(path, new ThrowingEncryptionHelper(writePartialFirst: true), ISimpleLogger.EmptyLogger);
            Assert.Throws<CryptographicException>(() => crashing.TrySave([Connection("OTHER")], priorLoadFailed: false));

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Load_WithCorruptMain_FallsBackToBackup()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            Assert.True(store.TrySave([Connection("SAVED")], priorLoadFailed: false));

            // First save also creates backups on subsequent saves; force one.
            Assert.True(store.TrySave([Connection("SAVED"), Connection("SECOND")], priorLoadFailed: false));
            Assert.True(File.Exists(path + ".bak.1"));

            File.WriteAllText(path, "not-encrypted-garbage{{{");

            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.RestoredFromBackup, store.Load(loaded));
            Assert.True(loaded.ContainsKey("SAVED"));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Load_WithMissingMain_FallsBackToBackup()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            Assert.True(store.TrySave([Connection("SAVED")], priorLoadFailed: false));
            Assert.True(store.TrySave([Connection("SAVED")], priorLoadFailed: false));
            File.Delete(path);

            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.RestoredFromBackup, store.Load(loaded));
            Assert.True(loaded.ContainsKey("SAVED"));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Load_WithCorruptMainAndNoBackup_ReportsFailed_AndRefusesBlindOverwrite()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            File.WriteAllText(path, "corrupt-bytes-not-json");
            byte[] before = File.ReadAllBytes(path);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Failed, store.Load(loaded));
            Assert.Empty(loaded);

            // The corrupt file must be preserved for forensics ...
            Assert.Single(Directory.GetFiles(directory, "*.corrupt-*"));

            // ... and a blind overwrite with empty/sample state is refused.
            Assert.False(store.TrySave([], priorLoadFailed: true));
            Assert.False(store.TrySave(
                [new LoginDataModel { ConnectionName = "SAMPLE_CONNECTION", Driver = "NetezzaSQL" }],
                priorLoadFailed: true));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void FailedLoad_FollowedByExplicitAdd_SavesNewState()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            File.WriteAllText(path, "corrupt-bytes-not-json");

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Failed, store.Load(loaded));

            // Explicit user edit after a failed load is intentional state:
            // the corrupt original stays preserved AND the new state is saved.
            Assert.True(store.TrySave([Connection("FRESH")], priorLoadFailed: true));

            var reloaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Loaded, store.Load(reloaded));
            Assert.Single(reloaded);
            Assert.True(reloaded.ContainsKey("FRESH"));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SnapshotOf_ReturnsIndependentCaseInsensitiveCopy()
    {
        var live = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["ONE"] = Connection("ONE"),
        };

        Dictionary<string, LoginDataModel> snapshot = CredentialsFileStore.SnapshotOf(live);

        Assert.NotSame(live, snapshot);
        Assert.True(snapshot.ContainsKey("one"));
        snapshot["TWO"] = Connection("TWO");
        snapshot.Remove("ONE");
        Assert.Single(live);
        Assert.True(live.ContainsKey("ONE"));
    }

    [Fact]
    public void Save_RotatesThreeBackupGenerations()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);

            for (int i = 1; i <= 5; i++)
            {
                Assert.True(store.TrySave([Connection($"GEN{i}")], priorLoadFailed: false));
            }

            Assert.True(File.Exists(path + ".bak.1"));
            Assert.True(File.Exists(path + ".bak.2"));
            Assert.True(File.Exists(path + ".bak.3"));
            Assert.False(File.Exists(path + ".bak.4"));

            // Newest backup holds the previous generation.
            var previous = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            var backupStore = new CredentialsFileStore(path + ".bak.1", new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            Assert.Equal(CredentialsLoadResult.Loaded, backupStore.Load(previous));
            Assert.True(previous.ContainsKey("GEN4"));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Load_AcceptsLegacyBareArrayFormat()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            File.WriteAllText(path, """[{"ConnectionName":"LEGACY","Driver":"NetezzaSQL","Server":"old.local"}]""");

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Loaded, store.Load(loaded));
            Assert.Equal("old.local", loaded["LEGACY"].Server);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Load_RejectsUnknownFutureVersion_AndPreservesFile()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            File.WriteAllText(path, """{"Version":999,"Connections":[]}""");
            byte[] before = File.ReadAllBytes(path);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.Failed, store.Load(loaded));
            Assert.Empty(loaded);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory, "*.corrupt-*"));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void Load_WithNoFile_ReportsNoFile_SoCallerSeedsSampleOnlyOnFirstRun()
    {
        string directory = NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);

            var loaded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.NoFile, store.Load(loaded));
            Assert.Empty(loaded);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }
}
