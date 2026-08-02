using System.Data;
using System.Data.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Services;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;

namespace JustyBase.Services.Documents;

public sealed class SqlConnectionManager : ISqlConnectionManager
{
    private readonly ISimpleLogger _logger;
    private readonly IMessageForUserTools _messageForUserTools;

    private record CachedDbConnection(IDatabaseService DbService, DbConnection Connection);

    private CachedDbConnection? _cached;
    private int _reconnectAttemptsUsed;

    public SqlConnectionManager(ISimpleLogger logger, IMessageForUserTools messageForUserTools)
    {
        _logger = logger;
        _messageForUserTools = messageForUserTools;
    }

    public bool HasOpenConnection => _cached?.Connection?.State == ConnectionState.Open;

    public int ReconnectAttemptsUsed => _reconnectAttemptsUsed;

    public DbConnection GetOrCreateConnection(bool doPooling, bool keepOpen, IDatabaseService service)
    {
        if (keepOpen && _cached is not null)
        {
            if (_cached.Connection.State == ConnectionState.Open)
            {
                return _cached.Connection;
            }

            // Cached keep-open connection died — one auto-recovery attempt (not perpetual keep-alive).
            if (ConnectionRecoveryPolicy.CanAttemptReconnect(_reconnectAttemptsUsed, isCancelled: false)
                && ConnectionRecoveryPolicy.IsBrokenConnection(ex: null, _cached.Connection.State))
            {
                _logger.TrackError(
                    new InvalidOperationException("Auto-recovery: recreating keep-open connection after break."),
                    isCrash: false);
                TryDisposeQuietly(_cached.Connection);
                _cached = null;
                _reconnectAttemptsUsed++;
                var recovered = service.GetConnection(null, pooling: doPooling);
                _cached = new CachedDbConnection(service, recovered);
                return recovered;
            }

            TryDisposeQuietly(_cached.Connection);
            _cached = null;
        }

        var con = service.GetConnection(null, pooling: doPooling);
        if (keepOpen)
        {
            _cached = new CachedDbConnection(service, con);
            _reconnectAttemptsUsed = 0;
        }

        return con;
    }

    /// <summary>
    /// Replaces a broken connection with a fresh one (single attempt). Returns null when cancelled or retry exhausted.
    /// </summary>
    public DbConnection? TryReconnectOnce(
        IDatabaseService service,
        DbConnection broken,
        bool doPooling,
        bool keepOpen,
        bool isCancelled,
        Exception? cause)
    {
        if (!ConnectionRecoveryPolicy.CanAttemptReconnect(_reconnectAttemptsUsed, isCancelled))
        {
            return null;
        }

        if (!ConnectionRecoveryPolicy.IsBrokenConnection(cause, broken.State))
        {
            return null;
        }

        _logger.TrackError(
            new InvalidOperationException("Auto-recovery: one reconnect attempt after broken connection.", cause),
            isCrash: false);
        _reconnectAttemptsUsed++;

        TryDisposeQuietly(broken);
        if (ReferenceEquals(_cached?.Connection, broken))
        {
            _cached = null;
        }

        var fresh = service.GetConnection(null, pooling: doPooling);
        if (keepOpen)
        {
            _cached = new CachedDbConnection(service, fresh);
        }

        return fresh;
    }

    public void ResetReconnectCounter() => _reconnectAttemptsUsed = 0;

    public void CloseConnection()
    {
        try
        {
            if (_cached?.Connection is { State: ConnectionState.Open })
            {
                _cached.Connection.Close();
            }
        }
        catch (Exception ex)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
            _logger.TrackError(ex, isCrash: false);
        }
        finally
        {
            _cached = null;
            _reconnectAttemptsUsed = 0;
        }
    }

    public void EmergencyCleanup(Func<Task> abortAction)
    {
        Task.Run(() =>
        {
            try
            {
                abortAction().Wait(TimeSpan.FromSeconds(5));
                _cached?.Connection?.Close();
            }
            catch (Exception ex1)
            {
                _logger.TrackError(ex1, isCrash: false);
                try
                {
                    if (_cached?.Connection is not null && _cached.DbService is INetezzaDotnet dService)
                    {
                        dService.DropConnectionEmergencyModeAsync(_cached.Connection).Wait(TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception ex2)
                {
                    _logger.TrackError(ex2, isCrash: false);
                    _messageForUserTools.ShowSimpleMessageBoxInstance(ex2.Message);
                }
            }
        }).ContinueWith(static x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void TryDisposeQuietly(DbConnection? connection)
    {
        try
        {
            connection?.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
