using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.PluginDatabaseBase.Database;

/// <summary>
/// Instance-owned registry for database service factories and connection caches.
/// Registered as a DI singleton; also exposed via <see cref="Shared"/> for plugin bootstrap.
/// </summary>
public sealed class DatabaseServiceRegistry
{
    private static DatabaseServiceRegistry? _shared;
    private static readonly Lock SharedInitLock = new();

    /// <summary>
    /// Process-wide registry used by static helpers and plugin loaders before/alongside DI.
    /// </summary>
    public static DatabaseServiceRegistry Shared
    {
        get
        {
            if (_shared is not null)
            {
                return _shared;
            }

            lock (SharedInitLock)
            {
                return _shared ??= new DatabaseServiceRegistry();
            }
        }
    }

    /// <summary>
    /// Ensures DI owns the same instance as <see cref="Shared"/> (call from composition root).
    /// </summary>
    public static DatabaseServiceRegistry UseSharedInstance() => Shared;

    private readonly Dictionary<string, IDatabaseService> _cachedDbServices = [];
    private readonly Lock _lockCachedDbServices = new();
    private readonly Dictionary<DatabaseTypeEnum, Func<string, string, string, string, string, int, IDatabaseService>> _implementations = [];
    private readonly Lock _lockImplementations = new();

    public event Action? SchemaCacheLoaded;

    private void NotifySchemaCacheLoaded() => SchemaCacheLoaded?.Invoke();

    public void AddDatabaseImplementation(
        DatabaseTypeEnum databaseTypeEnum,
        Func<string, string, string, string, string, int, IDatabaseService> ctorOfDbService)
    {
        lock (_lockImplementations)
        {
            _implementations[databaseTypeEnum] = ctorOfDbService;
        }
    }

    public bool HasDatabaseImplementation(DatabaseTypeEnum typedDriver)
    {
        lock (_lockImplementations)
        {
            return _implementations.ContainsKey(typedDriver);
        }
    }

    public void RemoveCachedConnection(string connectionName)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return;
        }

        IDatabaseService? removedService = null;
        lock (_lockCachedDbServices)
        {
            if (_cachedDbServices.TryGetValue(connectionName, out var cachedService))
            {
                removedService = cachedService;
                _cachedDbServices.Remove(connectionName);
            }
        }

        removedService?.ClearCachedData();
    }

    public IReadOnlyList<IDatabaseService> GetCachedServices()
    {
        lock (_lockCachedDbServices)
        {
            return _cachedDbServices.Values.ToList();
        }
    }

    public bool IsDatabaseConnected(string connectionName)
        => GetDatabaseConnectedLevel(connectionName) >= DatabaseConnectedLevel.Connected;

    public DatabaseConnectedLevel GetDatabaseConnectedLevel(string connectionName)
    {
        if (!TryGetCachedService(connectionName, out IDatabaseService? value))
        {
            return DatabaseConnectedLevel.NotConnected;
        }

        return value?.ConnectedLevel ?? DatabaseConnectedLevel.NotConnected;
    }

    public bool IsDriverRegistered(IDatabaseInfo? databaseInfo, string connectionName)
    {
        if (databaseInfo?.LoginDataDic is not null
            && databaseInfo.LoginDataDic.TryGetValue(connectionName, out var loginDataModel))
        {
            DatabaseTypeEnum typedDriver = DatabaseServiceHelpers.StringToDatabaseTypeEnum(loginDataModel.Driver);
            return HasDatabaseImplementation(typedDriver);
        }

        return false;
    }

    public IDatabaseService? GetDatabaseService(
        IDatabaseInfo? databaseInfo,
        string connectionName,
        bool forceRefresh = false,
        bool delayCache = false,
        int connectionTimeout = 0,
        Action<string>? messageAction = null,
        IDatabaseService? ownDatabaseService = null)
    {
        ArgumentNullException.ThrowIfNull(connectionName);

        if (forceRefresh)
        {
            RemoveCachedConnection(connectionName);
        }

        if (TryGetCachedService(connectionName, out var cachedService1))
        {
            return cachedService1;
        }

        IDatabaseService? databaseService;
        if (ownDatabaseService is null
            && databaseInfo?.LoginDataDic is not null
            && databaseInfo.LoginDataDic.TryGetValue(connectionName, out var loginDataModel))
        {
            string? userName = loginDataModel.UserName;
            string? password = loginDataModel.Password;
            string? ip = loginDataModel.Server;
            string? db = loginDataModel.Database;
            string driver = loginDataModel.Driver;
            loginDataModel.ConnectionName = connectionName;

            DatabaseTypeEnum typedDriver = DatabaseServiceHelpers.StringToDatabaseTypeEnum(driver);
            if (!HasDatabaseImplementation(typedDriver))
            {
                // Avoid sync-over-async deadlock when called from UI: load plugins on a worker and wait via ManualResetEventSlim.
                Exception? loadError = null;
                using var done = new ManualResetEventSlim(false);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await databaseInfo.LoadPluginsIfNeeded(null).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        loadError = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                if (!done.Wait(TimeSpan.FromSeconds(60))) // ManualResetEventSlim
                {
                    throw new TimeoutException($"Timed out loading plugins for driver '{typedDriver}'.");
                }
                if (loadError is not null)
                {
                    throw loadError;
                }
            }

            databaseService = CreateDbInstanceService(typedDriver, userName, password, ip, db, connectionTimeout, databaseInfo.GetDataDir());
            if (databaseService is ILoginDataAwareDatabaseService loginDataAwareDatabaseService)
            {
                loginDataAwareDatabaseService.ApplyLoginData(loginDataModel);
            }
        }
        else
        {
            databaseService = ownDatabaseService;
        }

        if (databaseService is null)
        {
            throw new InvalidOperationException("databaseService should not be null");
        }

        databaseService = CacheDatabaseService(connectionName, databaseService);
        databaseService.Logger = databaseInfo?.GlobalLoggerObject ?? ISimpleLogger.EmptyLogger;
        databaseService.ConnectedLevel = DatabaseConnectedLevel.Connected;
        databaseService.Name = connectionName;

        if (!delayCache)
        {
            try
            {
                databaseService.CacheMainDictionary();
                NotifySchemaCacheLoaded();
            }
            catch (Exception ex)
            {
                databaseService.Logger.TrackError(ex, isCrash: false);
                RemoveCachedService(connectionName);
                messageAction?.Invoke($"ERROR {ex.Message}");
                return null;
            }
        }
        else
        {
            Task.Run(() =>
            {
                try
                {
                    databaseService.CacheMainDictionary();
                    NotifySchemaCacheLoaded();
                }
                catch (Exception ex)
                {
                    databaseService.Logger.TrackError(ex, isCrash: false);
                    RemoveCachedService(connectionName);
                    messageAction?.Invoke($"ERROR {ex.Message}");
                }
            }).ContinueWith(static x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        return databaseService;
    }

    private IDatabaseService CreateDbInstanceService(
        DatabaseTypeEnum typedDriver,
        string userName,
        string password,
        string ip,
        string db,
        int connectionTimeout,
        string tempDirectory)
    {
        Func<string, string, string, string, string, int, IDatabaseService>? creator;
        lock (_lockImplementations)
        {
            _implementations.TryGetValue(typedDriver, out creator);
        }

        if (creator is null)
        {
            throw new NotSupportedException("database is not supported");
        }

        IDatabaseService databaseService = creator.Invoke(userName, password, "", ip, db, connectionTimeout);
        databaseService.TempDataDirectory = tempDirectory;
        return databaseService;
    }

    private bool TryGetCachedService(string connectionName, out IDatabaseService? databaseService)
    {
        lock (_lockCachedDbServices)
        {
            return _cachedDbServices.TryGetValue(connectionName, out databaseService);
        }
    }

    private IDatabaseService CacheDatabaseService(string connectionName, IDatabaseService databaseService)
    {
        lock (_lockCachedDbServices)
        {
            if (_cachedDbServices.TryGetValue(connectionName, out var cachedService))
            {
                return cachedService;
            }

            _cachedDbServices[connectionName] = databaseService;
            return databaseService;
        }
    }

    private void RemoveCachedService(string connectionName)
    {
        lock (_lockCachedDbServices)
        {
            _cachedDbServices.Remove(connectionName);
        }
    }
}
