using System.Collections.Concurrent;
using System.Data.Common;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public interface ISqlExecutionStateService
{
    int ActiveTasksCount { get; }
    int GlobalQueryNumber { get; }
    int GlobalAbortUpperBound { get; set; }

    int RegisterNewQuery();
    void TrackCommandState(int globalQueryNum, DbCommand cmd, SqlCommandState state);
    void MarkFullFinish(int globalQueryNum);
    Task AbortAllAsync();
}

public class SqlExecutionStateService : ISqlExecutionStateService
{
    private readonly ISimpleLogger _logger;
    private readonly ConcurrentDictionary<int, QueryInfo> _queriesDic = new();

    private int _globalQueryNumber = 0;
    public int GlobalQueryNumber => _globalQueryNumber;

    public int GlobalAbortUpperBound { get; set; } = 0;

    public int ActiveTasksCount
    {
        get
        {
            int count = 0;
            foreach (var queryInfo in _queriesDic.Values)
            {
                if (!queryInfo.FullFinish)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public SqlExecutionStateService(ISimpleLogger logger)
    {
        _logger = logger;
    }

    public int RegisterNewQuery()
    {
        _globalQueryNumber++;
        var actualNum = _globalQueryNumber;

        if (!_queriesDic.TryGetValue(actualNum, out var value))
        {
            value = new QueryInfo();
            _queriesDic[actualNum] = value;
        }

        return actualNum;
    }

    public void TrackCommandState(int globalQueryNum, DbCommand cmd, SqlCommandState state)
    {
        if (_queriesDic.TryGetValue(globalQueryNum, out var queryInfo))
        {
            queryInfo.DbCommands[cmd] = state;
        }
    }

    public void MarkFullFinish(int globalQueryNum)
    {
        if (_queriesDic.TryGetValue(globalQueryNum, out var queryInfo))
        {
            queryInfo.FullFinish = true;
        }
    }

    public async Task AbortAllAsync()
    {
        foreach (var queryInfo in _queriesDic.Values)
        {
            if (queryInfo.FullFinish)
                continue;

            queryInfo.RequestCancel = true;
            foreach (var kvp in queryInfo.DbCommands)
            {
                if (kvp.Value == SqlCommandState.started)
                {
                    try
                    {
                        kvp.Key.Cancel();
                    }
                    catch (Exception ex)
                    {
                        _logger.TrackError(ex, isCrash: false);
                    }
                }
            }
        }

        GlobalAbortUpperBound = _globalQueryNumber + 10;
        await Task.Delay(200);

        foreach (var queryInfo in _queriesDic.Values)
        {
            if (queryInfo.FullFinish) continue;
            foreach (var kvp in queryInfo.DbCommands)
            {
                if (kvp.Value == SqlCommandState.started)
                {
                    try
                    {
                        kvp.Key.Cancel();
                    }
                    catch (Exception ex)
                    {
                        _logger.TrackError(ex, isCrash: false);
                    }
                }
            }
        }

        await Task.Delay(1500);
        GlobalAbortUpperBound = _globalQueryNumber + 50000;
        await Task.Delay(500);

        _queriesDic.Clear();
    }
}
