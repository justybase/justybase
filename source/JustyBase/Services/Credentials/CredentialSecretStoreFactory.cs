using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Credentials;

/// <summary>
/// Selects the OS-native secret store. Unknown platforms get
/// <see cref="UnavailableSecretStore"/> so the app transparently uses file
/// storage instead of crashing.
/// </summary>
internal static class CredentialSecretStoreFactory
{
    public static ICredentialSecretStore Create(ISimpleLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore(logger);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainStore(logger);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretToolStore(logger);
        }

        return new UnavailableSecretStore();
    }
}

/// <summary>
/// File-fallback marker: the OS store is unreachable, every operation
/// reports failure and callers keep passwords in the encrypted file.
/// </summary>
internal sealed class UnavailableSecretStore : ICredentialSecretStore
{
    public bool IsAvailable => false;

    public bool TryGetSecret(string connectionName, out string? secret)
    {
        secret = null;
        return false;
    }

    public bool TrySetSecret(string connectionName, string secret) => false;

    public bool TryRemoveSecret(string connectionName) => false;
}
