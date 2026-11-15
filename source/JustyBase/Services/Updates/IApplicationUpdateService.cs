namespace JustyBase.Services.Updates;

public enum ApplicationUpdateStatus
{
    Unsupported,
    Disabled,
    Throttled,
    NoUpdate,
    Downloaded,
    PendingRestart,
    Cancelled,
    Failed
}

public sealed record ApplicationUpdateResult(
    ApplicationUpdateStatus Status,
    string? CurrentVersion = null,
    string? AvailableVersion = null,
    string? ErrorMessage = null);

public interface IApplicationUpdateService
{
    bool IsSupported { get; }
    bool HasPendingUpdate { get; }

    /// <summary>
    /// The Velopack channel the running installation was built for
    /// (<see cref="DistributionChannel.Full"/> or <see cref="DistributionChannel.Slim"/>),
    /// or null when this is not an installed Windows build.
    /// </summary>
    string? InstalledChannel { get; }

    Task<ApplicationUpdateResult> CheckAndDownloadAsync(
        bool manual,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the newest release of <paramref name="targetChannel"/> so the app can switch
    /// distribution variants. Unlike <see cref="CheckAndDownloadAsync"/>, this allows the other
    /// variant to carry the same or an older version, which is normal for a channel switch.
    /// </summary>
    Task<ApplicationUpdateResult> PrepareChannelSwitchAsync(
        string targetChannel,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    bool ApplyPendingUpdateAndRestart(string[]? restartArgs = null);

    /// <summary>
    /// Applies the package downloaded by <see cref="PrepareChannelSwitchAsync"/> and restarts.
    /// Returns false when no switch is prepared (for example after the app was restarted).
    /// </summary>
    bool ApplyPreparedChannelSwitchAndRestart(string[]? restartArgs = null);
}
