using JustyBase.Common.Contracts;
using JustyBase.Common.Helpers;
using System.Security.Cryptography;
using System.Text;

namespace JustyBase.Tests;

public class EncryptionHelperTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();
        var originalText = "TestSecret123!@#";

        var encrypted = helper.Encrypt(originalText);
        var decrypted = helper.Decrypt(encrypted);

        Assert.Equal(originalText, decrypted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Test with spaces")]
    [InlineData("Test123!@#$%^&*()_+-=[]{}|;':\",./<>?")]
    [InlineData("Unicode: żółćąśćń € 🎉")]
    public void EncryptDecrypt_VariousInputs_WorksCorrectly(string input)
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();

        var encrypted = helper.Encrypt(input);
        var decrypted = helper.Decrypt(encrypted);

        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void Encrypt_SameInput_DifferentOutputs()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();
        var input = "TestSecret";

        var encrypted1 = helper.Encrypt(input);
        var encrypted2 = helper.Encrypt(input);

        Assert.NotEqual(encrypted1, encrypted2);
        Assert.Equal(input, helper.Decrypt(encrypted1));
        Assert.Equal(input, helper.Decrypt(encrypted2));
    }

    [Fact]
    public void Encrypt_OutputsVersionedAdr005Format()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();

        byte[] blob = Convert.FromBase64String(helper.Encrypt("payload"));

        Assert.True(WindowsLinuxEncryptionHelper.IsCurrentFormat(blob));
        Assert.Equal((byte)'J', blob[0]);
        Assert.Equal((byte)'B', blob[1]);
        Assert.Equal((byte)'A', blob[2]);
        Assert.Equal((byte)'G', blob[3]);
        Assert.Equal((byte)1, blob[4]);
    }

    [Fact]
    public void Decrypt_LegacyFormat_StillWorks()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();
        const string original = "legacy-secret-ü";
        string legacyCiphertext = CreateLegacyCiphertext(original);

        Assert.False(WindowsLinuxEncryptionHelper.IsCurrentFormat(Convert.FromBase64String(legacyCiphertext)));
        Assert.Equal(original, helper.Decrypt(legacyCiphertext));
    }

    [Fact]
    public void SaveTextFileEncoded_WritesNewFormat_AndBacksUpOnce()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();
        string directory = Path.Combine(Path.GetTempPath(), "JustyBase.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "credentials.json.enc");
        string bakPath = path + ".bak";

        try
        {
            File.WriteAllText(path, CreateLegacyCiphertext("first-legacy"));

            helper.SaveTextFileEncoded(path, "second-current");
            helper.SaveTextFileEncoded(path, "third-current");

            Assert.True(File.Exists(bakPath));
            Assert.Equal("first-legacy", helper.Decrypt(File.ReadAllText(bakPath)));
            Assert.Equal("third-current", helper.Decrypt(File.ReadAllText(path)));
            Assert.True(WindowsLinuxEncryptionHelper.IsCurrentFormat(Convert.FromBase64String(File.ReadAllText(path))));
            // Backup is created only once — still holds the original legacy blob.
            Assert.False(WindowsLinuxEncryptionHelper.IsCurrentFormat(Convert.FromBase64String(File.ReadAllText(bakPath))));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Decrypt_InvalidData_ThrowsException()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();
        var invalidData = "InvalidBase64!!!";

        Assert.Throws<FormatException>(() => helper.Decrypt(invalidData));
    }

    [Fact]
    public void Decrypt_TamperedCurrentPayload_ThrowsCryptographicException()
    {
        IEncryptionHelper helper = new WindowsLinuxEncryptionHelper();
        byte[] blob = Convert.FromBase64String(helper.Encrypt("tamper-me"));
        blob[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => helper.Decrypt(Convert.ToBase64String(blob)));
    }

    private static string CreateLegacyCiphertext(string text)
    {
        byte[] originalText = Encoding.Unicode.GetBytes(text);

        if (OperatingSystem.IsWindows())
        {
            byte[] protectedBytes = ProtectedData.Protect(originalText, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        byte[] key = DeriveLegacyLinuxKey();
        byte[] iv = [0x96, 0x52, 0xd7, 0xa0, 0x1f, 0x7d, 0xee, 0x2d, 0x9b, 0x66, 0x0c, 0x96, 0x5c, 0x06, 0x5c, 0x69];
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        using MemoryStream memoryStream = new();
        using (CryptoStream cryptoStream = new(memoryStream, encryptor, CryptoStreamMode.Write))
        {
            cryptoStream.Write(originalText, 0, originalText.Length);
            cryptoStream.FlushFinalBlock();
        }

        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private static byte[] DeriveLegacyLinuxKey()
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
}
