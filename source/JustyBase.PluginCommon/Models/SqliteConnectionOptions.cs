using System.Text.Json.Serialization;

namespace JustyBase.PluginCommon.Models;

public sealed record SqliteAttachedDatabaseOptions
{
    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; }
}

public sealed record SqliteConnectionOptions
{
    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; }

    [JsonPropertyName("immutable")]
    public bool Immutable { get; init; }

    [JsonPropertyName("useUri")]
    public bool UseUri { get; init; }

    [JsonPropertyName("foreignKeys")]
    public bool ForeignKeys { get; init; } = true;

    [JsonPropertyName("busyTimeoutMilliseconds")]
    public int BusyTimeoutMilliseconds { get; init; } = 10_000;

    [JsonPropertyName("attachedDatabases")]
    public IReadOnlyList<SqliteAttachedDatabaseOptions> AttachedDatabases { get; init; } = Array.Empty<SqliteAttachedDatabaseOptions>();
}
