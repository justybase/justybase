namespace JustyBase.Common.Contracts;

/// <summary>
/// OS-backed per-connection secret storage (Windows Credential Manager,
/// Secret Service on Linux, Keychain on macOS), with graceful degradation:
/// every method returns false instead of throwing, so callers always fall
/// back to file storage. Implementations must normalize connection names
/// (trim + case-insensitive) so "MyConn" and "MYCONN" share one secret.
/// </summary>
public interface ICredentialSecretStore
{
    /// <summary>
    /// False when the OS store is unreachable (no D-Bus session, locked
    /// keychain, unsupported platform). Callers must use file fallback.
    /// </summary>
    bool IsAvailable { get; }

    bool TryGetSecret(string connectionName, out string? secret);

    bool TrySetSecret(string connectionName, string secret);

    bool TryRemoveSecret(string connectionName);
}
