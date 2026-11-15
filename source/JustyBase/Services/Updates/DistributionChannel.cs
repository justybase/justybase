namespace JustyBase.Services.Updates;

/// <summary>
/// Velopack release channels of the two JustyBase variants. Full and Slim share the
/// <c>JustyBase</c> package id, so the channel is what keeps their release feeds apart
/// and what an installed copy follows until it is switched explicitly.
/// </summary>
public static class DistributionChannel
{
    /// <summary>Full build: every database driver, AI features and the animated splash.</summary>
    public const string Full = "win";

    /// <summary>Slim (light) build: Netezza, SQLite, DB2, Postgres and MySQL, no AI.</summary>
    public const string Slim = "win-slim";

    /// <summary>Maps a user-facing variant name ("Full" / "Slim" / "light") to its Velopack channel.</summary>
    public static string? FromVariant(string? variant)
        => variant?.Trim().ToLowerInvariant() switch
        {
            "full" => Full,
            "slim" or "light" => Slim,
            _ => null,
        };

    /// <summary>Maps an installed Velopack channel to its user-facing variant name, or null for unknown channels.</summary>
    public static string? ToVariant(string? channel)
        => string.Equals(channel, Full, StringComparison.OrdinalIgnoreCase) ? "Full"
        : string.Equals(channel, Slim, StringComparison.OrdinalIgnoreCase) ? "Slim"
        : null;
}
