using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace JustyBase.Services.Credentials;

/// <summary>
/// macOS Keychain backend (Security.framework generic passwords).
/// Data symbols (kSec*, kCFBooleanTrue, dictionary callbacks) are resolved at
/// runtime via <see cref="NativeLibrary"/> instead of guessed P/Invoke fields,
/// and every failure degrades to false so callers fall back to file storage.
/// No-op off macOS.
/// </summary>
internal sealed class MacKeychainStore : ICredentialSecretStore
{
    private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8Encoding = 0x08000100;

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const int ErrSecDuplicateItem = -25299;

    private readonly ISimpleLogger _logger;
    private static readonly object SymbolsGate = new();
    private static NativeSymbols? _symbols;
    private static bool _probed;

    public MacKeychainStore(ISimpleLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAvailable => OperatingSystem.IsMacOS() && TryEnsureSymbols();

    public bool TryGetSecret(string connectionName, out string? secret)
    {
        secret = null;
        if (!TryNormalize(connectionName, out string? key) || !TryEnsureSymbols(out NativeSymbols? symbols))
        {
            return false;
        }

        IntPtr query = IntPtr.Zero;
        IntPtr result = IntPtr.Zero;
        try
        {
            query = symbols.BuildQuery(account: key, valueData: null, returnData: true);
            if (query == IntPtr.Zero)
            {
                return false;
            }

            int status = SecItemCopyMatching(query, out result);
            if (status != ErrSecSuccess || result == IntPtr.Zero)
            {
                return false;
            }

            nint length = CFDataGetLength(result);
            if (length <= 0)
            {
                return false;
            }

            byte[] bytes = new byte[(int)length];
            Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, bytes.Length);
            secret = Encoding.UTF8.GetString(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or OutOfMemoryException
            or UnauthorizedAccessException
            or IOException
            or EntryPointNotFoundException)
        {
            _logger.TrackError(ex, isCrash: false);
            secret = null;
            return false;
        }
        finally
        {
            if (result != IntPtr.Zero)
            {
                CFRelease(result);
            }

            if (query != IntPtr.Zero)
            {
                CFRelease(query);
            }
        }
    }

    public bool TrySetSecret(string connectionName, string secret)
    {
        if (!TryNormalize(connectionName, out string? key) || secret is null
            || !TryEnsureSymbols(out NativeSymbols? symbols))
        {
            return false;
        }

        byte[] blob = Encoding.UTF8.GetBytes(secret);
        if (blob.Length == 0)
        {
            return false;
        }

        IntPtr query = IntPtr.Zero;
        try
        {
            // Delete-then-add keeps the operation idempotent without SecItemUpdate.
            IntPtr deleteQuery = IntPtr.Zero;
            try
            {
                deleteQuery = symbols.BuildQuery(account: key, valueData: null, returnData: false);
                if (deleteQuery != IntPtr.Zero)
                {
                    _ = SecItemDelete(deleteQuery);
                }
            }
            finally
            {
                if (deleteQuery != IntPtr.Zero)
                {
                    CFRelease(deleteQuery);
                }
            }

            query = symbols.BuildQuery(account: key, valueData: blob, returnData: false);
            if (query == IntPtr.Zero)
            {
                return false;
            }

            return SecItemAdd(query, IntPtr.Zero) == ErrSecSuccess;
        }
        catch (Exception ex) when (ex is ArgumentException
            or OutOfMemoryException
            or UnauthorizedAccessException
            or IOException
            or EntryPointNotFoundException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            if (query != IntPtr.Zero)
            {
                CFRelease(query);
            }
        }
    }

    public bool TryRemoveSecret(string connectionName)
    {
        if (!TryNormalize(connectionName, out string? key) || !TryEnsureSymbols(out NativeSymbols? symbols))
        {
            return false;
        }

        IntPtr query = IntPtr.Zero;
        try
        {
            query = symbols.BuildQuery(account: key, valueData: null, returnData: false);
            if (query == IntPtr.Zero)
            {
                return false;
            }

            int status = SecItemDelete(query);
            // Missing entry is not an error: goal state (absent) already holds.
            return status is ErrSecSuccess or ErrSecItemNotFound;
        }
        catch (Exception ex) when (ex is ArgumentException
            or OutOfMemoryException
            or UnauthorizedAccessException
            or IOException
            or EntryPointNotFoundException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
        finally
        {
            if (query != IntPtr.Zero)
            {
                CFRelease(query);
            }
        }
    }

    private bool TryEnsureSymbols() => TryEnsureSymbols(out _);

    private static bool TryEnsureSymbols(out NativeSymbols? symbols)
    {
        if (_probed)
        {
            symbols = _symbols;
            return symbols is not null;
        }

        lock (SymbolsGate)
        {
            if (_probed)
            {
                symbols = _symbols;
                return symbols is not null;
            }

            _probed = true;
            try
            {
                if (!OperatingSystem.IsMacOS())
                {
                    symbols = null;
                    return false;
                }

                _symbols = NativeSymbols.Load();
                symbols = _symbols;
                return symbols is not null;
            }
            catch (Exception ex) when (ex is DllNotFoundException
                or EntryPointNotFoundException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException)
            {
                _symbols = null;
                symbols = null;
                return false;
            }
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

    private sealed class NativeSymbols
    {
        private const string ServiceName = "JustyBase";

        private readonly IntPtr _kSecClass;
        private readonly IntPtr _kSecClassGenericPassword;
        private readonly IntPtr _kSecAttrAccount;
        private readonly IntPtr _kSecAttrService;
        private readonly IntPtr _kSecValueData;
        private readonly IntPtr _kSecReturnData;
        private readonly IntPtr _kSecMatchLimit;
        private readonly IntPtr _kSecMatchLimitOne;
        private readonly IntPtr _kCFBooleanTrue;
        private readonly IntPtr _keyCallbacks;
        private readonly IntPtr _valueCallbacks;

        private NativeSymbols(
            IntPtr kSecClass, IntPtr kSecClassGenericPassword, IntPtr kSecAttrAccount,
            IntPtr kSecAttrService, IntPtr kSecValueData, IntPtr kSecReturnData,
            IntPtr kSecMatchLimit, IntPtr kSecMatchLimitOne, IntPtr kCFBooleanTrue,
            IntPtr keyCallbacks, IntPtr valueCallbacks)
        {
            _kSecClass = kSecClass;
            _kSecClassGenericPassword = kSecClassGenericPassword;
            _kSecAttrAccount = kSecAttrAccount;
            _kSecAttrService = kSecAttrService;
            _kSecValueData = kSecValueData;
            _kSecReturnData = kSecReturnData;
            _kSecMatchLimit = kSecMatchLimit;
            _kSecMatchLimitOne = kSecMatchLimitOne;
            _kCFBooleanTrue = kCFBooleanTrue;
            _keyCallbacks = keyCallbacks;
            _valueCallbacks = valueCallbacks;
        }

        public static NativeSymbols? Load()
        {
            if (!NativeLibrary.TryLoad(SecurityLib, out IntPtr sec)
                || !NativeLibrary.TryLoad(CoreFoundationLib, out IntPtr cf))
            {
                return null;
            }

            // Handles stay loaded for the process lifetime (symbols in use).
            if (!TryRead(sec, "kSecClass", out IntPtr kSecClass)
                || !TryRead(sec, "kSecClassGenericPassword", out IntPtr kSecGenPw)
                || !TryRead(sec, "kSecAttrAccount", out IntPtr kSecAttrAccount)
                || !TryRead(sec, "kSecAttrService", out IntPtr kSecAttrService)
                || !TryRead(sec, "kSecValueData", out IntPtr kSecValueData)
                || !TryRead(sec, "kSecReturnData", out IntPtr kSecReturnData)
                || !TryRead(sec, "kSecMatchLimit", out IntPtr kSecMatchLimit)
                || !TryRead(sec, "kSecMatchLimitOne", out IntPtr kSecMatchLimitOne)
                || !TryRead(cf, "kCFBooleanTrue", out IntPtr kCFBooleanTrue)
                || !NativeLibrary.TryGetExport(cf, "kCFTypeDictionaryKeyCallBacks", out IntPtr keyCB)
                || !NativeLibrary.TryGetExport(cf, "kCFTypeDictionaryValueCallBacks", out IntPtr valueCB))
            {
                return null;
            }

            return new NativeSymbols(
                kSecClass, kSecGenPw, kSecAttrAccount, kSecAttrService, kSecValueData,
                kSecReturnData, kSecMatchLimit, kSecMatchLimitOne, kCFBooleanTrue, keyCB, valueCB);

            static bool TryRead(IntPtr handle, string name, out IntPtr value)
            {
                value = IntPtr.Zero;
                return NativeLibrary.TryGetExport(handle, name, out IntPtr address)
                    && (value = Marshal.ReadIntPtr(address)) != IntPtr.Zero;
            }
        }

        public IntPtr BuildQuery(string account, byte[]? valueData, bool returnData)
        {
            var owned = new List<IntPtr>();
            try
            {
                var keys = new List<IntPtr> { _kSecClass, _kSecAttrAccount, _kSecAttrService };
                var values = new List<IntPtr>
                {
                    _kSecClassGenericPassword,
                    CreateOwned(owned, account),
                    CreateOwned(owned, ServiceName),
                };

                if (valueData is not null)
                {
                    keys.Add(_kSecValueData);
                    IntPtr dataValue = CFDataCreate(IntPtr.Zero, valueData, valueData.Length);
                    if (dataValue == IntPtr.Zero)
                    {
                        return IntPtr.Zero;
                    }

                    owned.Add(dataValue);
                    values.Add(dataValue);
                }

                if (returnData)
                {
                    keys.Add(_kSecReturnData);
                    values.Add(_kCFBooleanTrue);
                    keys.Add(_kSecMatchLimit);
                    values.Add(_kSecMatchLimitOne);
                }

                return CFDictionaryCreate(
                    IntPtr.Zero,
                    keys.ToArray(),
                    values.ToArray(),
                    keys.Count,
                    _keyCallbacks,
                    _valueCallbacks);
            }
            finally
            {
                // The dictionary retains what it needs; release our references.
                foreach (IntPtr ownedPtr in owned)
                {
                    CFRelease(ownedPtr);
                }
            }
        }

        private static IntPtr CreateOwned(List<IntPtr> owned, string value)
        {
            IntPtr created = CFString(value);
            if (created != IntPtr.Zero)
            {
                owned.Add(created);
            }

            return created;
        }

        private static IntPtr CFString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] nulTerminated = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, nulTerminated, 0, bytes.Length);
            return CFStringCreateWithCString(IntPtr.Zero, nulTerminated, Utf8Encoding);
        }
    }

    [DllImport(SecurityLib, EntryPoint = "SecItemAdd")]
    private static extern int SecItemAdd(IntPtr query, IntPtr result);

    [DllImport(SecurityLib, EntryPoint = "SecItemCopyMatching")]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityLib, EntryPoint = "SecItemDelete")]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(CoreFoundationLib, EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] cStr, uint encoding);

    [DllImport(CoreFoundationLib, EntryPoint = "CFDictionaryCreate")]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint numValues,
        IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundationLib, EntryPoint = "CFDataCreate")]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationLib, EntryPoint = "CFDataGetLength")]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationLib, EntryPoint = "CFDataGetBytePtr")]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationLib, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr obj);
}
