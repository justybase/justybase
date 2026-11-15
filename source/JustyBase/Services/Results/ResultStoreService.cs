using JustyBase.Common;

namespace JustyBase.Services.Results;

/// <summary>
/// Application-wide result storage services: the shared in-memory budget and
/// the spill-store manager. Registered once in DI; every result tab reserves
/// memory from the same budget and spills under the same disk quota.
/// </summary>
public sealed class ResultStoreService : IDisposable
{
    private static readonly Lazy<ResultStoreService> SharedLazy =
        new(() => new ResultStoreService(new AppOptions()));

    private bool _disposed;

    public ResultStoreService(AppOptions options)
        : this(options.ResultMemoryBudgetBytes, options.ResultSpillQuotaBytes)
    {
    }

    public ResultStoreService(long memoryBudgetBytes, long spillQuotaBytes)
    {
        Budget = new ResultMemoryBudget(memoryBudgetBytes);
        SpillManager = new SpillStoreManager(quotaBytes: spillQuotaBytes);
    }

    /// <summary>Fallback for call sites that do not run through DI (tests, tools).</summary>
    public static ResultStoreService Shared => SharedLazy.Value;

    public ResultMemoryBudget Budget { get; }

    public SpillStoreManager SpillManager { get; }

    public ResultSpillStore CreateSpillStore(ResultSpillSchema schema)
        => SpillManager.CreateStore(schema);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SpillManager.Dispose();
    }
}
