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

    Task<ApplicationUpdateResult> CheckAndDownloadAsync(
        bool manual,
        CancellationToken cancellationToken = default);

    bool ApplyPendingUpdateAndRestart(string[]? restartArgs = null);
}
