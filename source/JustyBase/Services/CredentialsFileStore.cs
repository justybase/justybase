using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace JustyBase.Services;

/// <summary>
/// Outcome of <see cref="CredentialsFileStore.Load"/>.
/// </summary>
internal enum CredentialsLoadResult
{
    Loaded,
    RestoredFromBackup,
    NoFile,
    Failed,
}

/// <summary>
/// File persistence for saved DB connections (credentials.json.enc).
/// All crash-safety lives here so it can be covered by regression tests:
/// atomic replace (never delete-then-write), rotating .bak generations with
/// legacy .bak fallback on load, .corrupt preservation, and refusal to
/// overwrite a failed load with an empty/sample-only snapshot.
/// </summary>
internal sealed class CredentialsFileStore
{
    private const int BackupGenerations = 3;
    private const string LegacyBackupSuffix = ".bak";

    private readonly string _filePath;
    private readonly IEncryptionHelper _encryptionHelper;
    private readonly ISimpleLogger _logger;

    public CredentialsFileStore(string filePath, IEncryptionHelper encryptionHelper, ISimpleLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _encryptionHelper = encryptionHelper ?? throw new ArgumentNullException(nameof(encryptionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string FilePath => _filePath;

    private string BackupPath(int generation) => $"{_filePath}.bak.{generation}";

    /// <summary>
    /// Point-in-time copy of the live credentials dictionary. Readers get this
    /// instead of the live instance so enumeration never races with mutations.
    /// </summary>
    internal static Dictionary<string, LoginDataModel> SnapshotOf(Dictionary<string, LoginDataModel> live)
    {
        ArgumentNullException.ThrowIfNull(live);
        return new Dictionary<string, LoginDataModel>(live, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads connections into <paramref name="target"/>. Never throws;
    /// <see cref="CredentialsLoadResult.Failed"/> means the on-disk content
    /// could not be read and was preserved for forensics.
    /// </summary>
    public CredentialsLoadResult Load(Dictionary<string, LoginDataModel> target)
    {
        ArgumentNullException.ThrowIfNull(target);

        bool mainExists = File.Exists(_filePath);
        if (mainExists && TryLoadFile(_filePath, target))
        {
            return CredentialsLoadResult.Loaded;
        }

        foreach (string backup in BackupCandidates())
        {
            if (TryLoadFile(backup, target))
            {
                if (mainExists)
                {
                    _logger.TrackError(
                        new IOException($"Credentials file was corrupt; restored from {Path.GetFileName(backup)} backup."),
                        isCrash: false);
                }

                return CredentialsLoadResult.RestoredFromBackup;
            }
        }

        if (!mainExists && !BackupCandidates().Any(File.Exists))
        {
            return CredentialsLoadResult.NoFile;
        }

        PreserveCorruptCopy();
        return CredentialsLoadResult.Failed;
    }

    private IEnumerable<string> BackupCandidates()
    {
        for (int generation = 1; generation <= BackupGenerations; generation++)
        {
            string path = BackupPath(generation);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        // Pre-rotation single backup written by older versions.
        string legacy = _filePath + LegacyBackupSuffix;
        if (File.Exists(legacy))
        {
            yield return legacy;
        }

        // Pre-keychain migration copy: lowest priority, last resort only.
        foreach (string preKeychain in PreKeychainCandidates())
        {
            yield return preKeychain;
        }
    }

    private IEnumerable<string> PreKeychainCandidates()
    {
        string? directory = Path.GetDirectoryName(_filePath);
        string fileName = Path.GetFileName(_filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        List<string> matches;
        try
        {
            matches = Directory.EnumerateFiles(directory, fileName + ".pre-keychain-*").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.TrackError(ex, isCrash: false);
            yield break;
        }

        matches.Sort(StringComparer.Ordinal);
        foreach (string match in matches)
        {
            yield return match;
        }
    }

    /// <summary>
    /// Atomically replaces the credentials file with <paramref name="snapshot"/>.
    /// Returns false (without touching the file) when a previous load failed and
    /// the snapshot holds no explicit user state — overwriting it would destroy
    /// possibly-recoverable data. Encryption/I-O failures propagate, but the
    /// previous file is always left intact (write goes to a temp file first).
    /// </summary>
    /// <remarks>
    /// Takes <see cref="List{T}"/> (not <c>IReadOnlyList</c>) because the
    /// source-generated serializer requires the exact declared type at runtime.
    /// </remarks>
    public bool TrySave(List<LoginDataModel> snapshot, bool priorLoadFailed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (priorLoadFailed)
        {
            // The on-disk file could not be decrypted/parsed at startup. It may
            // still be recoverable (different user, repaired DPAPI/machine-id),
            // so never write a bare sample over it from an empty in-memory state
            // without explicit user edits.
            bool onlySampleOrEmpty = snapshot.Count == 0
                || (snapshot.Count == 1 && string.Equals(snapshot[0].ConnectionName, "SAMPLE_CONNECTION", StringComparison.OrdinalIgnoreCase));
            if (onlySampleOrEmpty)
            {
                _logger.TrackError(
                    new IOException("Refusing to overwrite credentials file: previous load failed and no explicit connection changes were made."),
                    isCrash: false);
                return false;
            }

            PreserveCorruptCopy();
        }

        var envelope = new CredentialsFile
        {
            Version = CredentialsFile.CurrentVersion,
            Connections = snapshot,
        };
        string content = JsonSerializer.Serialize(envelope, MyJsonContextLoginDataModelList.Default.CredentialsFile);

        string tmp = _filePath + ".tmp";
        string tmpBak = tmp + ".bak";
        try
        {
            if (File.Exists(tmp))
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.TrackError(ex, isCrash: false);
                }
            }

            _encryptionHelper.SaveTextFileEncoded(tmp, content);

            RotateBackups();

            File.Move(tmp, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup: a leftover .tmp is harmless — it is
                // deleted on the next save and never read as credentials.
            }

            try
            {
                if (File.Exists(tmpBak))
                {
                    File.Delete(tmpBak);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup, same as above.
            }
        }

        return true;
    }

    /// <summary>
    /// Shifts .bak.1..N and stores the current main file as .bak.1.
    /// Best-effort: backup failures are logged but never block the save.
    /// </summary>
    private void RotateBackups()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            string oldest = BackupPath(BackupGenerations);
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (int generation = BackupGenerations - 1; generation >= 1; generation--)
            {
                string source = BackupPath(generation);
                if (File.Exists(source))
                {
                    File.Move(source, BackupPath(generation + 1), overwrite: true);
                }
            }

            File.Copy(_filePath, BackupPath(1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    private bool TryLoadFile(string path, Dictionary<string, LoginDataModel> target)
    {
        try
        {
            string plainText = _encryptionHelper.GetEncodedContentOfTextFile(path);
            List<LoginDataModel> credentialsList = ParseCredentials(plainText);
            target.Clear();
            foreach (LoginDataModel credentialItem in credentialsList)
            {
                target[credentialItem.ConnectionName.ToUpperInvariant()] = credentialItem;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or FormatException
            or JsonException
            or NotSupportedException)
        {
            // Expected load failures: unreadable/locked file, DPAPI or machine-key
            // unwrap failure, schema drift, or a newer unknown format version.
            // Logged once; the caller falls back to backups and preserves the file.
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    private static List<LoginDataModel> ParseCredentials(string plainText)
    {
        // Bare JSON array = pre-versioning legacy format (implicit version 1).
        foreach (char c in plainText)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (c == '[')
            {
                return JsonSerializer.Deserialize(plainText, MyJsonContextLoginDataModelList.Default.ListLoginDataModel) ?? [];
            }

            break;
        }

        CredentialsFile envelope = JsonSerializer.Deserialize(plainText, MyJsonContextLoginDataModelList.Default.CredentialsFile)
            ?? throw new JsonException("Credentials file envelope deserialized to null.");
        if (envelope.Version is < 1 or > CredentialsFile.CurrentVersion)
        {
            throw new NotSupportedException($"Unsupported credentials file version {envelope.Version}.");
        }

        return envelope.Connections;
    }

    private void PreserveCorruptCopy()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string preserved = $"{_filePath}.corrupt-{stamp}";
            if (!File.Exists(preserved))
            {
                File.Copy(_filePath, preserved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    /// <summary>
    /// One-time backup of the pre-keychain credentials file, taken before the
    /// first stripped (metadata-only) write. Never rotated or auto-deleted:
    /// it is the downgrade path if secret migration goes wrong.
    /// </summary>
    public void PreservePreKeychainCopy()
    {
        try
        {
            if (!File.Exists(_filePath) || PreKeychainCopyExists())
            {
                return;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Copy(_filePath, $"{_filePath}.pre-keychain-{stamp}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    private bool PreKeychainCopyExists()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            string fileName = Path.GetFileName(_filePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            return Directory.EnumerateFiles(directory, fileName + ".pre-keychain-*").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.TrackError(ex, isCrash: false);
            return true;
        }
    }
}
