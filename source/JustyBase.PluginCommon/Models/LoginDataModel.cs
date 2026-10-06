using System.Text.Json.Serialization;

namespace JustyBase.PluginCommon.Models;

public sealed class LoginDataModel
{
    public required string ConnectionName { get; set; }
    public required string Driver { get; set; }
    public string? Server { get; set; }
    [JsonPropertyName("port")]
    public string? Port { get; set; }
    public string? UserName { get; set; }
    
    /// <summary>
    /// CRITICAL SECURITY: This field contains sensitive credential data.
    /// NEVER expose this field to AI agents, logs, or any external output.
    /// Use ConnectionInfoSafe.FromLoginData() for safe data exposure to AI tools.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Where the password lives: "keychain" (OS secret store, <c>Password</c>
    /// is null in the file), "file" (password stored in the encrypted file as
    /// fallback), or null (legacy file written before secret migration).
    /// </summary>
    public string? SecretStorage { get; set; }

    public string? Database { get; set; }
    public string? Schema { get; set; }
    public string? Warehouse { get; set; }
    public string? Role { get; set; }
    public int? DefaultIndex { get; set; }
    [JsonPropertyName("sqliteOptions")]
    public SqliteConnectionOptions? SqliteOptions { get; set; }

    [JsonPropertyName("accessOptions")]
    public AccessConnectionOptions? AccessOptions { get; set; }
}

/// <summary>
/// Versioned envelope for the credentials file. Version 1 stores passwords
/// inside the encrypted file; version 2 stores connection metadata only and
/// keeps passwords in the OS secret store (per-connection
/// <c>LoginDataModel.SecretStorage</c> tells which). Unknown future versions
/// must be rejected (not guessed) by the loader so forward-incompatible files
/// are preserved instead of destroyed.
/// A bare JSON array (no envelope) is the pre-versioning legacy format and
/// loads as version 1.
/// </summary>
public sealed class CredentialsFile
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public List<LoginDataModel> Connections { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<LoginDataModel>))]
[JsonSerializable(typeof(CredentialsFile))]
public partial class MyJsonContextLoginDataModelList : JsonSerializerContext
{
}

[JsonSerializable(typeof(LoginDataModel))]
[JsonSerializable(typeof(SqliteConnectionOptions))]
[JsonSerializable(typeof(SqliteAttachedDatabaseOptions))]
[JsonSerializable(typeof(AccessConnectionOptions))]
public partial class MyJsonContextLoginDataModel : JsonSerializerContext
{
}
