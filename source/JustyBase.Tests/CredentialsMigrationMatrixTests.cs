using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using System.Text.Json;

namespace JustyBase.Tests;

/// <summary>
/// Phase B migration matrix: every historical on-disk format must reach the
/// final state (passwords in the OS store, metadata-only v2 file), including
/// jumps that skip intermediate app versions. The GAD orchestration
/// (load → <see cref="CredentialsSecretMigrator.Hydrate"/> →
/// <see cref="CredentialsSecretMigrator.StripForSave"/> → save) is exercised
/// here without instantiating GeneralApplicationData (which binds real
/// %AppData% paths); all file I/O stays in temp directories.
/// </summary>
public class CredentialsMigrationMatrixTests
{
    private static Dictionary<string, LoginDataModel> LoadAll(CredentialsFileStore store)
    {
        var dict = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
        store.Load(dict);
        return dict;
    }

    private static List<LoginDataModel> ReadEnvelopeConnections(string file)
    {
        string json = File.ReadAllText(file);
        CredentialsFile? envelope = JsonSerializer.Deserialize(json, MyJsonContextLoginDataModelList.Default.CredentialsFile);
        Assert.NotNull(envelope);
        return envelope.Connections;
    }

    [Fact]
    public void LegacyBareArray_MigratesSecrets_AndStripsFile()
    {
        string directory = CredentialsTestFixtures.NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            CredentialsTestFixtures.WriteLegacyBareArray(path, [CredentialsTestFixtures.Connection("ONE"), CredentialsTestFixtures.Connection("TWO")]);

            var encryption = new IdentityEncryptionHelper();
            var secrets = new InMemorySecretStore();
            var store = new CredentialsFileStore(path, encryption, ISimpleLogger.EmptyLogger);

            // Startup load (legacy format) + hydration.
            Dictionary<string, LoginDataModel> live = LoadAll(store);
            Assert.Equal(2, live.Count);
            int fallbacks = CredentialsSecretMigrator.Hydrate(live, secrets, ISimpleLogger.EmptyLogger);
            Assert.Equal(0, fallbacks);
            Assert.Equal("secret", live["ONE"].Password);

            // Save: secrets pushed first, file written stripped.
            List<LoginDataModel> save = CredentialsSecretMigrator.StripForSave([.. live.Values], secrets, ISimpleLogger.EmptyLogger, out bool strippedAny);
            Assert.True(strippedAny);
            Assert.True(store.TrySave(save, priorLoadFailed: false));

            List<LoginDataModel> fileConnections = ReadEnvelopeConnections(path);
            Assert.Equal(2, fileConnections.Count);
            Assert.All(fileConnections, c => Assert.Null(c.Password));
            Assert.All(fileConnections, c => Assert.Equal("keychain", c.SecretStorage));
            Assert.Equal("secret", secrets.Snapshot["ONE"]);
            Assert.Equal("secret", secrets.Snapshot["TWO"]);

            // Restart: stripped file + store hydrates passwords back.
            Dictionary<string, LoginDataModel> reloaded = LoadAll(store);
            Assert.Null(reloaded["ONE"].Password);
            int refallbacks = CredentialsSecretMigrator.Hydrate(reloaded, secrets, ISimpleLogger.EmptyLogger);
            Assert.Equal(0, refallbacks);
            Assert.Equal("secret", reloaded["ONE"].Password);
        }
        finally
        {
            CredentialsTestFixtures.DeleteTempDir(directory);
        }
    }

    [Fact]
    public void V1Envelope_MigratesSecrets_AndStripsFile()
    {
        string directory = CredentialsTestFixtures.NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            CredentialsTestFixtures.WriteEnvelope(path, 1, [CredentialsTestFixtures.Connection("ONE")]);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var secrets = new InMemorySecretStore();

            Dictionary<string, LoginDataModel> live = LoadAll(store);
            Assert.Equal(0, CredentialsSecretMigrator.Hydrate(live, secrets, ISimpleLogger.EmptyLogger));

            List<LoginDataModel> save = CredentialsSecretMigrator.StripForSave([.. live.Values], secrets, ISimpleLogger.EmptyLogger, out bool strippedAny);
            Assert.True(strippedAny);
            Assert.True(store.TrySave(save, priorLoadFailed: false));

            Assert.Null(ReadEnvelopeConnections(path)[0].Password);
            Assert.Equal("secret", secrets.Snapshot["ONE"]);
        }
        finally
        {
            CredentialsTestFixtures.DeleteTempDir(directory);
        }
    }

    [Fact]
    public void CorruptMain_ValidLegacyBackup_MigratesAfterRestore()
    {
        string directory = CredentialsTestFixtures.NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            CredentialsTestFixtures.WriteLegacyBareArray(path + ".bak", [CredentialsTestFixtures.Connection("FROMBAK")]);
            CredentialsTestFixtures.WriteGarbage(path);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var secrets = new InMemorySecretStore();

            var live = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CredentialsLoadResult.RestoredFromBackup, store.Load(live));
            Assert.Equal(0, CredentialsSecretMigrator.Hydrate(live, secrets, ISimpleLogger.EmptyLogger));
            Assert.Equal("secret", secrets.Snapshot["FROMBAK"]);
        }
        finally
        {
            CredentialsTestFixtures.DeleteTempDir(directory);
        }
    }

    [Fact]
    public void StoreUnavailable_PasswordsStayInFile()
    {
        string directory = CredentialsTestFixtures.NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            CredentialsTestFixtures.WriteLegacyBareArray(path, [CredentialsTestFixtures.Connection("ONE")]);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var secrets = new InMemorySecretStore { IsAvailable = false };

            Dictionary<string, LoginDataModel> live = LoadAll(store);
            Assert.Equal(0, CredentialsSecretMigrator.Hydrate(live, secrets, ISimpleLogger.EmptyLogger));
            Assert.Equal("secret", live["ONE"].Password);

            List<LoginDataModel> save = CredentialsSecretMigrator.StripForSave([.. live.Values], secrets, ISimpleLogger.EmptyLogger, out bool strippedAny);
            Assert.False(strippedAny);
            Assert.True(store.TrySave(save, priorLoadFailed: false));

            // File keeps the password; storage marker untouched (legacy null).
            List<LoginDataModel> fileConnections = ReadEnvelopeConnections(path);
            Assert.Equal("secret", fileConnections[0].Password);
            Assert.Null(fileConnections[0].SecretStorage);
        }
        finally
        {
            CredentialsTestFixtures.DeleteTempDir(directory);
        }
    }

    [Fact]
    public void PartialStoreFailure_DegradesPerConnection()
    {
        string directory = CredentialsTestFixtures.NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            CredentialsTestFixtures.WriteLegacyBareArray(
                path, [CredentialsTestFixtures.Connection("GOOD"), CredentialsTestFixtures.Connection("BAD")]);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var secrets = new InMemorySecretStore();
            secrets.FailSet.Add("BAD");

            Dictionary<string, LoginDataModel> live = LoadAll(store);
            int fallbacks = CredentialsSecretMigrator.Hydrate(live, secrets, ISimpleLogger.EmptyLogger);
            Assert.Equal(1, fallbacks);
            Assert.Equal("keychain", live["GOOD"].SecretStorage);
            Assert.Equal("file", live["BAD"].SecretStorage);

            List<LoginDataModel> save = CredentialsSecretMigrator.StripForSave([.. live.Values], secrets, ISimpleLogger.EmptyLogger, out bool strippedAny);
            Assert.True(strippedAny);
            Assert.True(store.TrySave(save, priorLoadFailed: false));

            List<LoginDataModel> fileConnections = ReadEnvelopeConnections(path);
            LoginDataModel good = fileConnections.Single(c => c.ConnectionName == "GOOD");
            LoginDataModel bad = fileConnections.Single(c => c.ConnectionName == "BAD");
            Assert.Null(good.Password);
            Assert.Equal("secret", bad.Password);
            Assert.Single(secrets.Snapshot);
        }
        finally
        {
            CredentialsTestFixtures.DeleteTempDir(directory);
        }
    }

    [Fact]
    public void MissingSecretInStore_CountsFallback_AndLeavesPasswordEmpty()
    {
        // Keychain wiped externally after migration: entry marked keychain,
        // password stripped, store has nothing.
        var live = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["GHOST"] = new LoginDataModel
            {
                ConnectionName = "GHOST",
                Driver = "NetezzaSQL",
                Password = null,
                SecretStorage = "keychain",
            },
        };

        int fallbacks = CredentialsSecretMigrator.Hydrate(live, new InMemorySecretStore(), ISimpleLogger.EmptyLogger);
        Assert.Equal(1, fallbacks);
        Assert.Null(live["GHOST"].Password);
    }

    [Fact]
    public void Downgrade_V1EraLoader_RejectsV2_RestoresBackupWithPasswords()
    {
        // Simulates an A-era build (understands bare arrays + v1 envelopes)
        // reading a directory after the final build saved v2. Rotation must
        // have preserved the last v1 file (with passwords) as .bak.1.
        string directory = CredentialsTestFixtures.NewTempDir();
        try
        {
            string path = Path.Combine(directory, "credentials.json.enc");
            CredentialsTestFixtures.WriteEnvelope(path, 1, [CredentialsTestFixtures.Connection("ONE")]);

            var store = new CredentialsFileStore(path, new IdentityEncryptionHelper(), ISimpleLogger.EmptyLogger);
            var secrets = new InMemorySecretStore();
            Dictionary<string, LoginDataModel> live = LoadAll(store);
            _ = CredentialsSecretMigrator.Hydrate(live, secrets, ISimpleLogger.EmptyLogger);
            List<LoginDataModel> save = CredentialsSecretMigrator.StripForSave([.. live.Values], secrets, ISimpleLogger.EmptyLogger, out _);
            Assert.True(store.TrySave(save, priorLoadFailed: false));

            // A-era loader: rejects Version > 1, falls back through backups.
            var downgraded = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
            bool loaded = V1EraLoad(path, downgraded);
            Assert.True(loaded);
            Assert.Equal("secret", downgraded["ONE"].Password);
        }
        finally
        {
            CredentialsTestFixtures.DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SecretStoreContract_NormalizesNames_AndRejectsBlanks()
    {
        var secrets = new InMemorySecretStore();
        Assert.True(secrets.TrySetSecret("  MixedCase ", "pw"));
        Assert.True(secrets.TryGetSecret("mixedcase", out string? found));
        Assert.Equal("pw", found);
        Assert.False(secrets.TryGetSecret("  ", out _));
        Assert.False(secrets.TrySetSecret("  ", "pw"));
        Assert.False(secrets.TryRemoveSecret(""));
    }

    /// <summary>
    /// Faithful simulation of the A-era loader: bare arrays and v1 envelopes
    /// only, newest backup chain first. Mirrors CredentialsFileStore.Load with
    /// CurrentVersion pinned to 1.
    /// </summary>
    private static bool V1EraLoad(string path, Dictionary<string, LoginDataModel> target)
    {
        List<string> candidates = [path, path + ".bak.1", path + ".bak.2", path + ".bak.3", path + ".bak"];
        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                string plainText = File.ReadAllText(candidate);
                List<LoginDataModel>? list = null;
                string trimmed = plainText.TrimStart();
                if (trimmed.StartsWith('['))
                {
                    list = JsonSerializer.Deserialize(trimmed, MyJsonContextLoginDataModelList.Default.ListLoginDataModel);
                }
                else
                {
                    CredentialsFile? envelope = JsonSerializer.Deserialize(trimmed, MyJsonContextLoginDataModelList.Default.CredentialsFile);
                    if (envelope is not null && envelope.Version == 1)
                    {
                        list = envelope.Connections;
                    }
                }

                if (list is null)
                {
                    continue;
                }

                target.Clear();
                foreach (LoginDataModel item in list)
                {
                    target[item.ConnectionName.ToUpperInvariant()] = item;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Try the next candidate, like the real fallback chain.
            }
        }

        return false;
    }
}
