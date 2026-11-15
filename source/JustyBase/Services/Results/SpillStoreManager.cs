using System.Diagnostics;

namespace JustyBase.Services.Results;

/// <summary>
/// Owns the application-wide temporary-storage quota for spilled results and
/// removes spill files left behind by earlier runs.
///
/// The manager never deletes a live result: when the quota is exhausted the
/// store stops appending and reports <see cref="SpillQuotaExceededException"/>,
/// so the grid keeps the rows that were already spilled. The LRU cache below
/// evicts decoded pages, not user data.
/// </summary>
public sealed class SpillStoreManager : IDisposable
{
    public const long DefaultQuotaBytes = 2L * 1024 * 1024 * 1024;
    public const long DefaultPageCacheBytes = 64L * 1024 * 1024;

    private const string FilePrefix = "spill_";
    private static readonly TimeSpan OrphanMaxAge = TimeSpan.FromHours(24);

    private readonly string _root;
    private readonly long _quotaBytes;
    private readonly Lock _gate = new();
    private readonly HashSet<ResultSpillStore> _stores = [];
    private long _usedBytes;
    private bool _disposed;

    public SpillStoreManager(
        string? directory = null,
        long quotaBytes = DefaultQuotaBytes,
        long pageCacheBytes = DefaultPageCacheBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotaBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCacheBytes);

        _root = directory ?? Path.Combine(Path.GetTempPath(), "JustyBaseSpill");
        _quotaBytes = quotaBytes;
        Directory.CreateDirectory(_root);
        Cache = new SpillPageCache(pageCacheBytes);
        CleanupOrphanFiles();
    }

    public string Root => _root;

    public long QuotaBytes => _quotaBytes;

    public long UsedBytes => Interlocked.Read(ref _usedBytes);

    public long AvailableBytes => Math.Max(0, QuotaBytes - UsedBytes);

    internal SpillPageCache Cache { get; }

    public ResultSpillStore CreateStore(ResultSpillSchema schema)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var store = new ResultSpillStore(schema, this, _root);
        lock (_gate)
        {
            _stores.Add(store);
        }

        return store;
    }

    internal bool TryReserve(long bytes)
    {
        if (bytes <= 0)
        {
            return true;
        }

        while (true)
        {
            long current = Interlocked.Read(ref _usedBytes);
            long next = current + bytes;
            if (next > QuotaBytes)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _usedBytes, next, current) == current)
            {
                return true;
            }
        }
    }

    internal void Release(long bytes)
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

    internal void RemoveStore(ResultSpillStore store)
    {
        lock (_gate)
        {
            _stores.Remove(store);
        }
    }

    /// <summary>
    /// Deletes orphaned spill files from previous runs. A file is considered
    /// orphaned when its owning process is gone, or when it is older than
    /// <see cref="OrphanMaxAge"/>. Files owned by the current process are never
    /// touched. Returns the number of deleted files.
    /// </summary>
    public int CleanupOrphanFiles()
    {
        int deleted = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, FilePrefix + "*"))
            {
                string candidate = StripSidecarSuffix(file);
                if (!TryGetOwnerProcessId(candidate, out int pid))
                {
                    continue;
                }

                if (pid == Environment.ProcessId)
                {
                    continue;
                }

                bool processGone = !IsProcessAlive(pid);
                bool tooOld = DateTime.UtcNow - File.GetLastWriteTimeUtc(candidate) > OrphanMaxAge;
                if (!processGone && !tooOld)
                {
                    continue;
                }

                if (TryDelete(file))
                {
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; a locked file is retried on the next run.
        }

        return deleted;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ResultSpillStore[] stores;
        lock (_gate)
        {
            stores = [.. _stores];
            _stores.Clear();
        }

        foreach (ResultSpillStore store in stores)
        {
            store.Dispose();
        }

        Cache.Clear();
    }

    private static string StripSidecarSuffix(string path)
    {
        if (path.EndsWith("-wal", StringComparison.Ordinal)
            || path.EndsWith("-shm", StringComparison.Ordinal))
        {
            return path[..^4];
        }

        return path;
    }

    private static bool TryGetOwnerProcessId(string path, out int pid)
    {
        pid = 0;
        string name = Path.GetFileName(path);
        if (!name.StartsWith(FilePrefix, StringComparison.Ordinal)
            || !name.EndsWith(".db", StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = name[FilePrefix.Length..^3];
        int separator = remainder.IndexOf('_', StringComparison.Ordinal);
        return separator > 0
            && int.TryParse(remainder[..separator], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out pid)
            && pid > 0;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }

        return false;
    }
}

internal readonly record struct SpillPageKey(
    string StoreId,
    long Version,
    int PageIndex,
    int PageSize,
    string SortSignature);

/// <summary>
/// Bounded LRU cache of decoded spill pages shared by every result tab. The
/// cache keeps scrolling cheap without letting many open results pin an
/// unbounded number of decoded rows in memory.
/// </summary>
internal sealed class SpillPageCache
{
    private sealed record Entry(SpillPageKey Key, object?[][] Rows, long Bytes);

    private readonly long _capacityBytes;
    private readonly Lock _gate = new();
    private readonly Dictionary<SpillPageKey, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _usedBytes;

    public SpillPageCache(long capacityBytes)
    {
        _capacityBytes = capacityBytes;
    }

    public long UsedBytes
    {
        get
        {
            lock (_gate)
            {
                return _usedBytes;
            }
        }
    }

    public bool TryGet(SpillPageKey key, out object?[][] rows)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                rows = node.Value.Rows;
                return true;
            }
        }

        rows = [];
        return false;
    }

    public void Set(SpillPageKey key, object?[][] rows, long bytes)
    {
        if (bytes <= 0 || bytes > _capacityBytes)
        {
            return;
        }

        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                _lru.Remove(existing);
                _usedBytes -= existing.Value.Bytes;
                _map.Remove(key);
            }

            while (_usedBytes + bytes > _capacityBytes && _lru.Last is not null)
            {
                LinkedListNode<Entry> last = _lru.Last;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _usedBytes -= last.Value.Bytes;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, rows, bytes));
            _lru.AddFirst(node);
            _map[key] = node;
            _usedBytes += bytes;
        }
    }

    public void RemoveStore(string storeId)
    {
        lock (_gate)
        {
            var stale = _map.Keys.Where(key => string.Equals(key.StoreId, storeId, StringComparison.Ordinal)).ToList();
            foreach (SpillPageKey key in stale)
            {
                LinkedListNode<Entry> node = _map[key];
                _lru.Remove(node);
                _map.Remove(key);
                _usedBytes -= node.Value.Bytes;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _usedBytes = 0;
        }
    }
}
