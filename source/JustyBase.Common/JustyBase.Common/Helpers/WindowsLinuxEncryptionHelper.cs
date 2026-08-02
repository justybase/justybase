using JustyBase.Common.Contracts;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace JustyBase.Common.Helpers;

/// <summary>
/// Credential encryption per ADR-005: AES-256-GCM with a random data key per payload.
/// Windows seals the data key with DPAPI (CurrentUser). Linux/macOS wrap the data key
/// with AES-GCM using a machine-id–derived wrapping key (not plaintext on disk).
/// Legacy Avalonia blobs (whole-payload DPAPI on Windows; fixed-IV AES-CBC on Linux) remain readable.
/// </summary>
public sealed class WindowsLinuxEncryptionHelper : IEncryptionHelper
{
    private static readonly byte[] CurrentMagic = "JBAG"u8.ToArray();
    private const byte CurrentVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int AesKeySize = 32;
    private const int MaxSealedKeySize = 4096;
    private const int MaxPayloadSize = 64 * 1024 * 1024;

    /// <summary>Legacy Linux AES-CBC key material (machine-id SHA-256 or hostname fallback).</summary>
    private static readonly byte[] LegacyLinuxKey;

    /// <summary>Legacy Linux AES-CBC IV (fixed; kept only for decrypting pre-ADR-005 blobs).</summary>
    private static readonly byte[] LegacyLinuxIv =
        [0x96, 0x52, 0xd7, 0xa0, 0x1f, 0x7d, 0xee, 0x2d, 0x9b, 0x66, 0x0c, 0x96, 0x5c, 0x06, 0x5c, 0x69];

    /// <summary>
    /// Wrapping key for non-Windows platforms: SHA-256 of machine-id (or hostname).
    /// Documented limitation: same machine can unwrap; not equivalent to DPAPI user binding.
    /// </summary>
    private static readonly byte[] MachineWrappingKey;

    static WindowsLinuxEncryptionHelper()
    {
        byte[] hashData = DeriveMachineKeyMaterial();
        LegacyLinuxKey = hashData;
        MachineWrappingKey = hashData;
    }

    public string Encrypt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] plaintext = Encoding.UTF8.GetBytes(text);
        byte[] aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length];

        try
        {
            using (var aes = new AesGcm(aesKey, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAssociatedData());
            }

            byte[] sealedKey = SealKey(aesKey);
            byte[] payload = BuildCurrentPayload(sealedKey, nonce, tag, ciphertext);
            return Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Decrypt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] blob = Convert.FromBase64String(text);
        if (IsCurrentFormat(blob))
        {
            return DecryptCurrent(blob);
        }

        return DecryptLegacy(blob);
    }

    public string GetEncodedContentOfTextFile(string realFilePath)
    {
        string content = File.ReadAllText(realFilePath);
        return Decrypt(content);
    }

    public void SaveTextFileEncoded(string filePath, string fileContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(fileContent);

        if (File.Exists(filePath))
        {
            string backupPath = filePath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(filePath, backupPath);
            }
        }

        File.WriteAllText(filePath, Encrypt(fileContent));
    }

    public static bool IsCurrentFormat(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < CurrentMagic.Length + 1)
        {
            return false;
        }

        return blob[..CurrentMagic.Length].SequenceEqual(CurrentMagic)
            && blob[CurrentMagic.Length] == CurrentVersion;
    }

    private static string DecryptCurrent(byte[] blob)
    {
        int offset = 0;
        offset += CurrentMagic.Length;
        byte version = blob[offset++];
        if (version != CurrentVersion)
        {
            throw new CryptographicException("Unsupported credentials format version.");
        }

        if (blob.Length < offset + sizeof(int))
        {
            throw new CryptographicException("Invalid credentials header.");
        }

        int sealedKeyLength = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(offset));
        offset += sizeof(int);
        if (sealedKeyLength <= 0 || sealedKeyLength > MaxSealedKeySize
            || blob.Length < offset + sealedKeyLength + NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid credentials key length.");
        }

        byte[] sealedKey = blob.AsSpan(offset, sealedKeyLength).ToArray();
        offset += sealedKeyLength;

        byte[] nonce = blob.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        byte[] tag = blob.AsSpan(offset, TagSize).ToArray();
        offset += TagSize;

        int cipherLength = blob.Length - offset;
        if (cipherLength < 0 || cipherLength > MaxPayloadSize)
        {
            throw new CryptographicException("Invalid credentials payload length.");
        }

        byte[] ciphertext = blob.AsSpan(offset, cipherLength).ToArray();
        byte[] aesKey = UnsealKey(sealedKey);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(aesKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAssociatedData());
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string DecryptLegacy(byte[] encryptedText)
    {
        byte[]? originalText;
        if (!OperatingSystem.IsWindows())
        {
            using Aes aes = Aes.Create();
            aes.Key = LegacyLinuxKey;
            aes.IV = LegacyLinuxIv;
            using ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            originalText = PerformCryptography(encryptedText, decryptor);
        }
        else
        {
            originalText = ProtectedData.Unprotect(encryptedText, null, DataProtectionScope.CurrentUser);
        }

        return Encoding.Unicode.GetString(originalText);
    }

    private static byte[] SealKey(byte[] aesKey)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(aesKey, BuildAssociatedData(), DataProtectionScope.CurrentUser);
        }

        // Machine-id wrapping: AES-GCM seal of the random data key (never stored in plaintext).
        byte[] wrapNonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] wrapTag = new byte[TagSize];
        byte[] wrapCiphertext = new byte[aesKey.Length];
        using (var aes = new AesGcm(MachineWrappingKey, TagSize))
        {
            aes.Encrypt(wrapNonce, aesKey, wrapCiphertext, wrapTag, BuildAssociatedData());
        }

        byte[] sealedKey = new byte[NonceSize + TagSize + wrapCiphertext.Length];
        wrapNonce.CopyTo(sealedKey, 0);
        wrapTag.CopyTo(sealedKey, NonceSize);
        wrapCiphertext.CopyTo(sealedKey, NonceSize + TagSize);
        return sealedKey;
    }

    private static byte[] UnsealKey(byte[] sealedKey)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(sealedKey, BuildAssociatedData(), DataProtectionScope.CurrentUser);
        }

        if (sealedKey.Length < NonceSize + TagSize + AesKeySize)
        {
            throw new CryptographicException("Invalid wrapped credentials key.");
        }

        byte[] wrapNonce = sealedKey.AsSpan(0, NonceSize).ToArray();
        byte[] wrapTag = sealedKey.AsSpan(NonceSize, TagSize).ToArray();
        byte[] wrapCiphertext = sealedKey.AsSpan(NonceSize + TagSize).ToArray();
        byte[] aesKey = new byte[wrapCiphertext.Length];
        using var aes = new AesGcm(MachineWrappingKey, TagSize);
        aes.Decrypt(wrapNonce, wrapCiphertext, wrapTag, aesKey, BuildAssociatedData());
        return aesKey;
    }

    private static byte[] BuildCurrentPayload(byte[] sealedKey, byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        byte[] payload = new byte[CurrentMagic.Length + 1 + sizeof(int) + sealedKey.Length + NonceSize + TagSize + ciphertext.Length];
        int offset = 0;
        CurrentMagic.CopyTo(payload, offset);
        offset += CurrentMagic.Length;
        payload[offset++] = CurrentVersion;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), sealedKey.Length);
        offset += sizeof(int);
        sealedKey.CopyTo(payload, offset);
        offset += sealedKey.Length;
        nonce.CopyTo(payload, offset);
        offset += NonceSize;
        tag.CopyTo(payload, offset);
        offset += TagSize;
        ciphertext.CopyTo(payload, offset);
        return payload;
    }

    private static byte[] BuildAssociatedData()
    {
        return [CurrentMagic[0], CurrentMagic[1], CurrentMagic[2], CurrentMagic[3], CurrentVersion];
    }

    private static byte[] DeriveMachineKeyMaterial()
    {
        string[] machineIdPaths =
        [
            @"/var/lib/dbus/machine-id",
            @"/var/db/dbus/machine-id",
            @"/etc/machine-id"
        ];

        foreach (string path in machineIdPaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return SHA256.HashData(File.ReadAllBytes(path));
            }
            catch (IOException)
            {
                // Try next path
            }
            catch (UnauthorizedAccessException)
            {
                // Try next path
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName));
    }

    private static byte[] PerformCryptography(byte[] data, ICryptoTransform cryptoTransform)
    {
        using MemoryStream memoryStream = new();
        using (CryptoStream cryptoStream = new(memoryStream, cryptoTransform, CryptoStreamMode.Write))
        {
            cryptoStream.Write(data, 0, data.Length);
            cryptoStream.FlushFinalBlock();
            return memoryStream.ToArray();
        }
    }
}
