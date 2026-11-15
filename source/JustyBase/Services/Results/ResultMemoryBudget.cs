namespace JustyBase.Services.Results;

/// <summary>
/// Application-wide budget for decoded result rows. Every result tab reserves
/// the estimated size of the rows it keeps in memory and releases the share
/// when the result is replaced or closed. The budget is independent from the
/// per-result row limit: a query may be below its own limit and still spill
/// because other retained results already use the shared budget.
/// </summary>
public sealed class ResultMemoryBudget
{
    private long _limitBytes;
    private long _usedBytes;

    public ResultMemoryBudget(long limitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);
        _limitBytes = limitBytes;
    }

    public long LimitBytes => Interlocked.Read(ref _limitBytes);

    public long UsedBytes => Interlocked.Read(ref _usedBytes);

    public long AvailableBytes => Math.Max(0, LimitBytes - UsedBytes);

    /// <summary>
    /// Reserves <paramref name="bytes"/> when the shared budget has room.
    /// Returns <see langword="false"/> when the caller should switch to spill
    /// instead of failing the query.
    /// </summary>
    public bool TryReserve(long bytes)
    {
        if (bytes <= 0)
        {
            return true;
        }

        while (true)
        {
            long current = Interlocked.Read(ref _usedBytes);
            long next = current + bytes;
            if (next > LimitBytes)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _usedBytes, next, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>Releases bytes previously reserved by <see cref="TryReserve"/>.</summary>
    public void Release(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        while (true)
        {
            long current = Interlocked.Read(ref _usedBytes);
            long next = Math.Max(0, current - bytes);
            if (Interlocked.CompareExchange(ref _usedBytes, next, current) == current)
            {
                return;
            }
        }
    }
}
