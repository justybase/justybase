using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;

namespace JustyBase.Services;

/// <summary>
/// Phase B secret migration between the credentials file and the OS secret
/// store. Pure logic over caller-provided collections (no file I/O here),
/// so the full version matrix is unit-testable. All operations are
/// per-connection best-effort: one bad entry never blocks the rest.
/// </summary>
internal static class CredentialsSecretMigrator
{
    public const string SecretStorageKeychain = "keychain";
    public const string SecretStorageFile = "file";

    /// <summary>
    /// Post-load hydration: imports file passwords into the store and refills
    /// stripped entries from it. Returns the number of entries that fell back
    /// to file storage (store write failed or secret missing).
    /// </summary>
    public static int Hydrate(
        Dictionary<string, LoginDataModel> connections,
        ICredentialSecretStore secretStore,
        ISimpleLogger logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(logger);

        int fallbacks = 0;
        if (!secretStore.IsAvailable)
        {
            return fallbacks;
        }

        foreach (var (key, login) in connections)
        {
            try
            {
                if (!string.IsNullOrEmpty(login.Password))
                {
                    // Legacy/file entry: import into the store. The file copy
                    // is stripped on the next save.
                    if (secretStore.TrySetSecret(key, login.Password))
                    {
                        login.SecretStorage = SecretStorageKeychain;
                    }
                    else
                    {
                        login.SecretStorage = SecretStorageFile;
                        fallbacks++;
                    }
                }
                else if (string.Equals(login.SecretStorage, SecretStorageKeychain, StringComparison.OrdinalIgnoreCase))
                {
                    if (secretStore.TryGetSecret(key, out string? secret) && !string.IsNullOrEmpty(secret))
                    {
                        login.Password = secret;
                    }
                    else
                    {
                        fallbacks++;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                logger.TrackError(ex, isCrash: false);
                fallbacks++;
            }
        }

        return fallbacks;
    }

    /// <summary>
    /// Builds save copies with passwords moved to the OS secret store.
    /// Live entries keep their passwords for the session; only the returned
    /// copies are stripped. Returns the copies plus whether any password was
    /// stripped (caller takes the one-time .pre-keychain backup then).
    /// </summary>
    public static List<LoginDataModel> StripForSave(
        List<LoginDataModel> live,
        ICredentialSecretStore secretStore,
        ISimpleLogger logger,
        out bool strippedAny)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(logger);

        var save = new List<LoginDataModel>(live.Count);
        strippedAny = false;
        if (!secretStore.IsAvailable)
        {
            // No OS store: persist entries unchanged (passwords stay in the
            // encrypted file, storage markers untouched).
            foreach (LoginDataModel item in live)
            {
                save.Add(CloneForSave(item, stripPassword: false, item.SecretStorage));
            }

            return save;
        }

        foreach (LoginDataModel item in live)
        {
            if (string.IsNullOrEmpty(item.Password))
            {
                save.Add(CloneForSave(item, stripPassword: true, item.SecretStorage ?? SecretStorageKeychain));
                continue;
            }

            if (secretStore.TrySetSecret(item.ConnectionName, item.Password))
            {
                item.SecretStorage = SecretStorageKeychain;
                save.Add(CloneForSave(item, stripPassword: true, SecretStorageKeychain));
                strippedAny = true;
            }
            else
            {
                item.SecretStorage = SecretStorageFile;
                save.Add(CloneForSave(item, stripPassword: false, SecretStorageFile));
            }
        }

        return save;
    }

    private static LoginDataModel CloneForSave(LoginDataModel source, bool stripPassword, string? secretStorage)
    {
        return new LoginDataModel
        {
            ConnectionName = source.ConnectionName,
            Driver = source.Driver,
            Server = source.Server,
            Port = source.Port,
            UserName = source.UserName,
            Password = stripPassword ? null : source.Password,
            SecretStorage = secretStorage,
            Database = source.Database,
            Schema = source.Schema,
            Warehouse = source.Warehouse,
            Role = source.Role,
            DefaultIndex = source.DefaultIndex,
            SqliteOptions = source.SqliteOptions,
            AccessOptions = source.AccessOptions,
        };
    }
}
