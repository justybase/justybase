using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace JustyBase.Services.Updates;

public sealed class ApplicationUpdateService : IApplicationUpdateService, IDisposable
{
    private const string GitHubRepositoryUrl = "https://github.com/justybase/justybase";
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private UpdateManager? _updateManager;
    private PreparedChannelSwitch? _preparedChannelSwitch;
    private bool _managerCreationFailed;

    private sealed record PreparedChannelSwitch(UpdateManager Manager, VelopackAsset Asset);

    public ApplicationUpdateService(
        IGeneralApplicationData generalApplicationData,
        ISimpleLogger simpleLogger)
    {
        _generalApplicationData = generalApplicationData;
        _simpleLogger = simpleLogger;
    }

    public bool IsSupported => TryGetUpdateManager() is { IsInstalled: true };

    public bool HasPendingUpdate
        => _preparedChannelSwitch is not null || TryGetUpdateManager()?.UpdatePendingRestart is not null;

    public string? InstalledChannel
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            // TryGetUpdateManager swallows its own failures, and Current is only read
            // after IsCurrentSet confirms VelopackApp.Run() created the locator.
            UpdateManager? updateManager = TryGetUpdateManager();
            return updateManager is { IsInstalled: true } && VelopackLocator.IsCurrentSet
                ? VelopackLocator.Current.Channel
                : null;
        }
    }

    public async Task<ApplicationUpdateResult> CheckAndDownloadAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(ApplicationUpdateStatus.Unsupported);
        }

        UpdateManager? updateManager = TryGetUpdateManager();
        if (updateManager is null || !updateManager.IsInstalled)
        {
            return new(ApplicationUpdateStatus.Unsupported);
        }

        if (!manual && !_generalApplicationData.Config.AutoDownloadUpdate)
        {
            return new(ApplicationUpdateStatus.Disabled);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!manual
            && _generalApplicationData.Config.LastUpdateCheckUtc is { } lastCheck
            && now - lastCheck < AutomaticCheckInterval)
        {
            return new(ApplicationUpdateStatus.Throttled,
                updateManager.CurrentVersion?.ToString());
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A manual check may race with the automatic check started by MainWindow.Loaded.
            // Re-check the persisted timestamp after acquiring the gate.
            if (!manual
                && _generalApplicationData.Config.LastUpdateCheckUtc is { } lockedLastCheck
                && DateTimeOffset.UtcNow - lockedLastCheck < AutomaticCheckInterval)
            {
                return new(ApplicationUpdateStatus.Throttled,
                    updateManager.CurrentVersion?.ToString());
            }

            _generalApplicationData.Config.LastUpdateCheckUtc = now;
            _generalApplicationData.SaveAppConfig();

            VelopackAsset? pending = updateManager.UpdatePendingRestart;
            if (pending is not null)
            {
                return new(
                    ApplicationUpdateStatus.PendingRestart,
                    updateManager.CurrentVersion?.ToString(),
                    pending.Version.ToString());
            }

            UpdateInfo? updateInfo = await updateManager
                .CheckForUpdatesAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (updateInfo is null)
            {
                return new(ApplicationUpdateStatus.NoUpdate,
                    updateManager.CurrentVersion?.ToString());
            }

            string availableVersion = updateInfo.TargetFullRelease.Version.ToString();
            await updateManager.DownloadUpdatesAsync(
                    updateInfo,
                    progress: null,
                    cancelToken: cancellationToken)
                .ConfigureAwait(false);

            return new(
                ApplicationUpdateStatus.Downloaded,
                updateManager.CurrentVersion?.ToString(),
                availableVersion);
        }
        catch (OperationCanceledException)
        {
            return new(ApplicationUpdateStatus.Cancelled,
                updateManager.CurrentVersion?.ToString());
        }
        catch (Exception exception)
        {
            _simpleLogger.TrackError(exception, isCrash: false);
            return new(
                ApplicationUpdateStatus.Failed,
                updateManager.CurrentVersion?.ToString(),
                ErrorMessage: exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplicationUpdateResult> PrepareChannelSwitchAsync(
        string targetChannel,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetChannel);

        if (!OperatingSystem.IsWindows())
        {
            return new(ApplicationUpdateStatus.Unsupported);
        }

        UpdateManager? installed = TryGetUpdateManager();
        if (installed is null || !installed.IsInstalled)
        {
            return new(ApplicationUpdateStatus.Unsupported);
        }

        if (string.Equals(InstalledChannel, targetChannel, StringComparison.OrdinalIgnoreCase))
        {
            return new(ApplicationUpdateStatus.NoUpdate, installed.CurrentVersion?.ToString());
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _preparedChannelSwitch = null;

            // A dedicated manager keeps the explicit channel out of the normal update path.
            // Downgrades must be allowed: the other variant can be at the same or an older
            // version, and the switch is a lateral move between channels.
            var switchManager = new UpdateManager(
                new GithubSource(GitHubRepositoryUrl, accessToken: null, prerelease: true),
                new UpdateOptions
                {
                    ExplicitChannel = targetChannel,
                    AllowVersionDowngrade = true,
                });

            UpdateInfo? updateInfo = await switchManager
                .CheckForUpdatesAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (updateInfo is null)
            {
                return new(ApplicationUpdateStatus.NoUpdate, installed.CurrentVersion?.ToString());
            }

            Action<int>? reportProgress = progress is null ? null : progress.Report;
            await switchManager.DownloadUpdatesAsync(
                    updateInfo,
                    reportProgress,
                    cancellationToken)
                .ConfigureAwait(false);

            string availableVersion = updateInfo.TargetFullRelease.Version.ToString();
            _preparedChannelSwitch = new PreparedChannelSwitch(switchManager, updateInfo.TargetFullRelease);

            return new(
                ApplicationUpdateStatus.Downloaded,
                installed.CurrentVersion?.ToString(),
                availableVersion);
        }
        catch (OperationCanceledException)
        {
            return new(ApplicationUpdateStatus.Cancelled,
                installed.CurrentVersion?.ToString());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _simpleLogger.TrackError(exception, isCrash: false);
            return new(
                ApplicationUpdateStatus.Failed,
                installed.CurrentVersion?.ToString(),
                ErrorMessage: exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public bool ApplyPendingUpdateAndRestart(string[]? restartArgs = null)
    {
        UpdateManager? updateManager = TryGetUpdateManager();
        VelopackAsset? pending = updateManager?.UpdatePendingRestart;
        if (updateManager is null || pending is null)
        {
            return false;
        }

        try
        {
            updateManager.ApplyUpdatesAndRestart(pending, restartArgs);
            return true;
        }
        catch (Exception exception)
        {
            _simpleLogger.TrackError(exception, isCrash: false);
            return false;
        }
    }

    public bool ApplyPreparedChannelSwitchAndRestart(string[]? restartArgs = null)
    {
        // Velopack's UpdatePendingRestart only reports packages newer than the installed
        // version, so a same-version channel switch must apply the asset we downloaded.
        if (_preparedChannelSwitch is not { } prepared)
        {
            return false;
        }

        try
        {
            prepared.Manager.ApplyUpdatesAndRestart(prepared.Asset, restartArgs);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _preparedChannelSwitch = null;
            _simpleLogger.TrackError(exception, isCrash: false);
            return false;
        }
    }

    public void Dispose()
    {
        _operationGate.Dispose();
    }

    private UpdateManager? TryGetUpdateManager()
    {
        if (!OperatingSystem.IsWindows() || _managerCreationFailed)
        {
            return null;
        }

        if (_updateManager is not null)
        {
            return _updateManager;
        }

        try
        {
            _updateManager = new UpdateManager(
                new GithubSource(GitHubRepositoryUrl, accessToken: null, prerelease: true));
            return _updateManager;
        }
        catch (Exception exception)
        {
            _managerCreationFailed = true;
            _simpleLogger.TrackError(exception, isCrash: false);
            return null;
        }
    }
}
