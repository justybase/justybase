using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace JustyBase.Services.Credentials;

/// <summary>
/// Windows Credential Manager backend (advapi32 Cred*W, generic credentials,
/// persisted per local machine). Never throws: every failure degrades to false
/// so callers fall back to file storage. No-op on non-Windows platforms.
/// P/Invoke stays as DllImport on purpose: the source-generated LibraryImport
/// cannot marshal a non-blittable struct by ref (SYSLIB1051), while classic
/// DllImport signatures are statically analyzable and therefore already
/// Native AOT and trim compatible.
/// </summary>
internal sealed class WindowsCredentialStore : ICredentialSecretStore
{
    private const string TargetPrefix = "JustyBase/credentials/";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int MaxBlobBytes = 512;

    private readonly ISimpleLogger _logger;

    public WindowsCredentialStore(ISimpleLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool TryGetSecret(string connectionName, out string? secret)
    {
        secret = null;
        if (!IsAvailable || !TryNormalize(connectionName, out string? key))
        {
            return false;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!CredReadW(TargetPrefix + key, CredTypeGeneric, 0, out buffer) || buffer == IntPtr.Zero)
            {
                return false;
            }

            // AOT-safe unmarshalling: Marshal.PtrToStructure<T> cannot marshal
            // non-blittable structs (string fields) under Native AOT — there is
            // no runtime marshaller to generate the layout glue. The CREDENTIALW
            // layout is a stable Windows ABI, so read the two needed fields
            // directly. Only the blob size and blob pointer are consumed.
            int blobSizeOffset = IntPtr.Size == 8
                ? CredentialBlobSizeOffset
                : CredentialBlobSizeOffsetX86;
            int blobOffset = IntPtr.Size == 8
                ? CredentialBlobOffset
                : CredentialBlobOffsetX86;

            int blobSize = Marshal.ReadInt32(buffer, blobSizeOffset);
            IntPtr blobPtr = Marshal.ReadIntPtr(buffer, blobOffset);
            if (blobSize <= 0 || blobPtr == IntPtr.Zero)
            {
                return false;
            }

            byte[] blob = new byte[blobSize];
            Marshal.Copy(blobPtr, blob, 0, blob.Length);
            secret = Encoding.Unicode.GetString(blob).TrimEnd('\0');
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or EntryPointNotFoundException
            or UnauthorizedAccessException
            or IOException
            or ArgumentException
            or OutOfMemoryException)
        {
            _logger.TrackError(ex, isCrash: false);
            secret = null;
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                CredFree(buffer);
            }
        }
    }

    public bool TrySetSecret(string connectionName, string secret)
    {
        if (!IsAvailable || !TryNormalize(connectionName, out string? key) || secret is null)
        {
            return false;
        }

        byte[] blob = Encoding.Unicode.GetBytes(secret);
        if (blob.Length == 0 || blob.Length > MaxBlobBytes)
        {
            return false;
        }

        IntPtr blobPtr = IntPtr.Zero;
        try
        {
            blobPtr = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new NativeCredential
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = TargetPrefix + key,
                Comment = "JustyBase saved database connection",
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = key,
            };
            return CredWriteW(ref credential, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or EntryPointNotFoundException
            or UnauthorizedAccessException
            or IOException
            or ArgumentException
            or OutOfMemoryException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
        finally
        {
            if (blobPtr != IntPtr.Zero)
            {
                for (int i = 0; i < blob.Length; i++)
                {
                    Marshal.WriteByte(blobPtr, i, 0);
                }

                Marshal.FreeHGlobal(blobPtr);
            }

            CryptographicOperations.ZeroMemory(blob);
        }
    }

    public bool TryRemoveSecret(string connectionName)
    {
        if (!IsAvailable || !TryNormalize(connectionName, out string? key))
        {
            return false;
        }

        try
        {
            // Missing entry is not an error: goal state (absent) already holds.
            return CredDeleteW(TargetPrefix + key, CredTypeGeneric, 0)
                || Marshal.GetLastWin32Error() == 1168; // ERROR_NOT_FOUND
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or EntryPointNotFoundException
            or UnauthorizedAccessException
            or IOException
            or ArgumentException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    private static bool TryNormalize(string? connectionName, out string key)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            key = string.Empty;
            return false;
        }

        key = connectionName.Trim().ToUpperInvariant();
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    /// <summary>
    /// Byte offsets of CREDENTIALW fields on 64-bit Windows
    /// (Flags:0 Type:4 TargetName:8 Comment:16 LastWritten:24
    /// CredentialBlobSize:32 [+4 padding] CredentialBlob:40 ...).
    /// Used by the AOT-safe manual reader above; the struct itself is only
    /// ever passed to CredWriteW, whose DllImport marshaller AOT generates
    /// statically from the fixed signature.
    /// </summary>
    private const int CredentialBlobSizeOffset = 32;
    private const int CredentialBlobOffset = 40;
    private const int CredentialBlobSizeOffsetX86 = 24;
    private const int CredentialBlobOffsetX86 = 28;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW([In] ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string targetName, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string targetName, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
