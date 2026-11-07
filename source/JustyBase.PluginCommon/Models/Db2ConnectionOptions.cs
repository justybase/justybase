namespace JustyBase.PluginCommon.Models;

/// <summary>
/// Db2 LUW connection options stored beside the generic login data. They map to
/// <c>CurrentSchema</c>, TLS (<c>UseSSL</c>) and <c>StatementTimeout</c> in the
/// managed IBM-compatible connection string builder.
/// </summary>
public sealed record Db2ConnectionOptions
{
    /// <summary>Default schema for unqualified names (<c>CurrentSchema</c>).</summary>
    public string? CurrentSchema { get; init; }

    /// <summary>Enable TLS for the DRDA connection.</summary>
    public bool UseSsl { get; init; }

    /// <summary>Per-statement timeout in seconds; null keeps the driver default.</summary>
    public int? StatementTimeoutSeconds { get; init; }
}
