using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Models;
using System.Text.Json;

namespace JustyBase.Tests;

/// <summary>
/// Shared fixtures for credentials migration-matrix tests (Phase B).
/// All file I/O stays inside temp directories — never the real %AppData%.
/// </summary>
internal sealed class IdentityEncryptionHelper : IEncryptionHelper
{
    public string Decrypt(string text) => text;
    public string Encrypt(string text) => text;
    public string GetEncodedContentOfTextFile(string realFilePath) => File.ReadAllText(realFilePath);
    public void SaveTextFileEncoded(string filePath, string fileContent) => File.WriteAllText(filePath, fileContent);
}

/// <summary>
/// In-memory <see cref="ICredentialSecretStore"/> fake for migration tests.
/// </summary>
internal sealed class InMemorySecretStore : ICredentialSecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);
    public bool IsAvailable { get; set; } = true;

    /// <summary>Connection names (case-insensitive) for which TrySetSecret fails.</summary>
    public HashSet<string> FailSet { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetSecret(string connectionName, out string? secret)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            secret = null;
            return false;
        }

        return _secrets.TryGetValue(connectionName.Trim(), out secret);
    }

    public bool TrySetSecret(string connectionName, string secret)
    {
        if (string.IsNullOrWhiteSpace(connectionName) || secret is null)
        {
            return false;
        }

        if (FailSet.Contains(connectionName.Trim()))
        {
            return false;
        }

        _secrets[connectionName.Trim()] = secret;
        return true;
    }

    public bool TryRemoveSecret(string connectionName)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return false;
        }

        _secrets.Remove(connectionName.Trim());
        return true;
    }

    public IReadOnlyDictionary<string, string> Snapshot => _secrets;
}

internal static class CredentialsTestFixtures
{
    public static string NewTempDir()
    {
        string directory = Path.Combine(Path.GetTempPath(), "JustyBase.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void DeleteTempDir(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static LoginDataModel Connection(string name, string? password = "secret") => new()
    {
        ConnectionName = name,
        Driver = "NetezzaSQL",
        Server = "db.example.com",
        Port = "5480",
        Database = "mydb",
        UserName = "user",
        Password = password,
    };

    /// <summary>Pre-A format: bare JSON array, no envelope.</summary>
    public static void WriteLegacyBareArray(string path, List<LoginDataModel> connections)
    {
        string json = JsonSerializer.Serialize(connections, MyJsonContextLoginDataModelList.Default.ListLoginDataModel);
        File.WriteAllText(path, json);
    }

    /// <summary>Versioned envelope format (v1 and synthetic future versions).</summary>
    public static void WriteEnvelope(string path, int version, List<LoginDataModel> connections)
    {
        string json = JsonSerializer.Serialize(
            new CredentialsFile { Version = version, Connections = connections },
            MyJsonContextLoginDataModelList.Default.CredentialsFile);
        File.WriteAllText(path, json);
    }

    public static void WriteGarbage(string path) => File.WriteAllText(path, "not-json-garbage{{{");
}
