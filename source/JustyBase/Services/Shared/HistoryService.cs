using System.Text.Json;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using System.Text.Json.Serialization;

namespace JustyBase.Common.Services;

public sealed class HistoryService(
    IGeneralApplicationData generalApplicationData,
    ISimpleLogger? simpleLogger = null)
{
    private readonly IGeneralApplicationData _generalApplicationData = generalApplicationData;
    private readonly ISimpleLogger _simpleLogger = simpleLogger ?? ISimpleLogger.EmptyLogger;
    private readonly HistoryFileStore _historyFileStore = new(
        IGeneralApplicationData.HistoryDatFilePath,
        Path.Combine(Path.GetDirectoryName(IGeneralApplicationData.HistoryDatFilePath)!, "history.favorites.json"),
        Path.Combine(Path.GetDirectoryName(IGeneralApplicationData.HistoryDatFilePath)!, "history.runs.json"));

    private List<HistoryEntry>? _historyEntries;
    private HashSet<string>? _favoriteKeys;
    private Dictionary<string, HistoryRunMeta>? _runMetaByKey;

    /// <summary>Monotonic timestamps so FavoriteKey stays unique under coarse DateTime.Now resolution.</summary>
    private DateTime _lastHistoryDateTime = DateTime.MinValue;

    public event EventHandler? HistoryChanged;

    public List<HistoryEntry>? HistoryItemsCollection
    {
        get
        {
            if (_historyEntries is null)
            {
                LoadFromFileToList();
            }
            return _historyEntries;
        }
    }

    private bool Loaded => _historyEntries is not null;
    private readonly Lock _sync = new();

    private string FavoritesFilePath =>
        Path.Combine(Path.GetDirectoryName(IGeneralApplicationData.HistoryDatFilePath)!, "history.favorites.json");

    private string RunsMetaFilePath =>
        Path.Combine(Path.GetDirectoryName(IGeneralApplicationData.HistoryDatFilePath)!, "history.runs.json");

    private void LoadFromFileToList()
    {
        lock (_sync)
        {
            if (Loaded) // load only one time
            {
                return;
            }
            _historyEntries = [];
            IReadOnlyList<HistoryFileRecord> records = [];
            try
            {
                records = _historyFileStore.Load();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _simpleLogger.TrackError(ex, isCrash: false);
            }

            _favoriteKeys = LoadFavoriteKeysUnlocked();
            _runMetaByKey = LoadRunMetaUnlocked();

            foreach (HistoryFileRecord record in records)
            {
                try
                {
                    var logDateTime = DateTime.FromBinary(record.DateBinary);
                    var entry = new HistoryEntry()
                    {
                        Date = logDateTime,
                        Database = record.Database,
                        Connection = record.Connection,
                        SQL = record.Sql
                    };
                    if (logDateTime >= DateTime.Now.AddMonths(-_generalApplicationData.Config.LimitHistoryMonths))
                    {
                        entry.IsFavorite = _favoriteKeys.Contains(entry.FavoriteKey);
                        ApplyRunMetaUnlocked(entry);
                        _historyEntries.Add(entry);
                        if (entry.Date > _lastHistoryDateTime)
                        {
                            _lastHistoryDateTime = entry.Date;
                        }
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore an invalid record while keeping all other records available.
                }
            }

            PruneRunMetaToLiveEntriesUnlocked();
        }
    }

    public void AddHistoryEntry(
        string sql,
        string baza,
        string connectioName,
        HistoryRunStatus status = HistoryRunStatus.Unknown,
        long? durationMs = null,
        string? errorMessage = null)
    {
        bool raiseChanged;
        lock (_sync)
        {
            var currentDateTime = NextUniqueHistoryTimestampUnlocked();
            _historyFileStore.Append(new HistoryFileRecord
            {
                DateBinary = currentDateTime.ToBinary(),
                Sql = sql,
                Database = baza,
                Connection = connectioName,
            });

            var entry = new HistoryEntry()
            {
                Date = currentDateTime,
                Database = baza,
                Connection = connectioName,
                SQL = sql,
                Status = status,
                DurationMs = durationMs,
                ErrorMessage = errorMessage,
            };

            if (status != HistoryRunStatus.Unknown || durationMs is not null || !string.IsNullOrWhiteSpace(errorMessage))
            {
                _runMetaByKey ??= LoadRunMetaUnlocked();
                _runMetaByKey[entry.FavoriteKey] = new HistoryRunMeta
                {
                    Status = status,
                    DurationMs = durationMs,
                    ErrorMessage = errorMessage,
                };
                SaveRunMetaUnlocked();
            }

            raiseChanged = Loaded;
            if (Loaded)
            {
                entry.IsFavorite = (_favoriteKeys ??= []).Contains(entry.FavoriteKey);
                _historyEntries!.Add(entry);
            }
        }

        if (raiseChanged)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IEnumerable<HistoryEntry> Filter(
        string? searchTxt,
        bool favoritesOnly = false,
        HistoryRunStatus? statusFilter = null,
        HistoryDurationPreset durationPreset = HistoryDurationPreset.All)
    {
        var items = HistoryItemsCollection;
        if (items is null)
        {
            return [];
        }

        return items.Where(e => e.FiltrerRow(searchTxt ?? "", favoritesOnly, statusFilter, durationPreset));
    }

    public void SetFavorite(HistoryEntry entry, bool isFavorite)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            _favoriteKeys ??= LoadFavoriteKeysUnlocked();
            entry.IsFavorite = isFavorite;
            if (isFavorite)
            {
                _favoriteKeys.Add(entry.FavoriteKey);
            }
            else
            {
                _favoriteKeys.Remove(entry.FavoriteKey);
            }

            SaveFavoriteKeysUnlocked();
        }
    }

    public void ToggleFavorite(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        SetFavorite(entry, !entry.IsFavorite);
    }

    /// <summary>
    /// Ensures each new history row gets a DateTime strictly greater than the previous one,
    /// so FavoriteKey (derived from Date) cannot collide when DateTime.Now resolution is coarse.
    /// </summary>
    private DateTime NextUniqueHistoryTimestampUnlocked()
    {
        var now = DateTime.Now;
        if (now <= _lastHistoryDateTime)
        {
            now = _lastHistoryDateTime.AddTicks(1);
        }

        _lastHistoryDateTime = now;
        return now;
    }

    private void ApplyRunMetaUnlocked(HistoryEntry entry)
    {
        if (_runMetaByKey is null || !_runMetaByKey.TryGetValue(entry.FavoriteKey, out var meta))
        {
            return;
        }

        entry.Status = meta.Status;
        entry.DurationMs = meta.DurationMs;
        entry.ErrorMessage = meta.ErrorMessage;
    }

    private void PruneRunMetaToLiveEntriesUnlocked()
    {
        if (_runMetaByKey is null || _historyEntries is null || _runMetaByKey.Count == 0)
        {
            return;
        }

        var liveKeys = _historyEntries.Select(e => e.FavoriteKey).ToHashSet(StringComparer.Ordinal);
        var stale = _runMetaByKey.Keys.Where(k => !liveKeys.Contains(k)).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var key in stale)
        {
            _runMetaByKey.Remove(key);
        }

        SaveRunMetaUnlocked();
    }

    private HashSet<string> LoadFavoriteKeysUnlocked()
    {
        try
        {
            if (!File.Exists(FavoritesFilePath))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(FavoritesFilePath);
            var list = JsonSerializer.Deserialize(json, HistoryJsonContext.Default.ListString) ?? [];
            return new HashSet<string>(list, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void SaveFavoriteKeysUnlocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FavoritesFilePath)!);
            var json = JsonSerializer.Serialize(_favoriteKeys?.ToList() ?? [], HistoryJsonContext.Default.ListString);
            File.WriteAllText(FavoritesFilePath, json);
        }
        catch
        {
            // favorites are best-effort sidecar
        }
    }

    private Dictionary<string, HistoryRunMeta> LoadRunMetaUnlocked()
    {
        try
        {
            if (!File.Exists(RunsMetaFilePath))
            {
                return new Dictionary<string, HistoryRunMeta>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(RunsMetaFilePath);
            var list = JsonSerializer.Deserialize(json, HistoryJsonContext.Default.ListHistoryRunMetaRecord) ?? [];
            var dict = new Dictionary<string, HistoryRunMeta>(StringComparer.Ordinal);
            foreach (var record in list)
            {
                if (string.IsNullOrWhiteSpace(record.Key))
                {
                    continue;
                }

                dict[record.Key] = new HistoryRunMeta
                {
                    Status = record.Status,
                    DurationMs = record.DurationMs,
                    ErrorMessage = record.ErrorMessage,
                };
            }

            return dict;
        }
        catch
        {
            return new Dictionary<string, HistoryRunMeta>(StringComparer.Ordinal);
        }
    }

    private void SaveRunMetaUnlocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RunsMetaFilePath)!);
            var list = (_runMetaByKey ?? []).Select(kv => new HistoryRunMetaRecord
            {
                Key = kv.Key,
                Status = kv.Value.Status,
                DurationMs = kv.Value.DurationMs,
                ErrorMessage = kv.Value.ErrorMessage,
            }).ToList();
            var json = JsonSerializer.Serialize(list, HistoryJsonContext.Default.ListHistoryRunMetaRecord);
            File.WriteAllText(RunsMetaFilePath, json);
        }
        catch
        {
            // run meta is best-effort sidecar
        }
    }

    private sealed class HistoryRunMeta
    {
        public HistoryRunStatus Status { get; set; }
        public long? DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
    }

    internal sealed class HistoryRunMetaRecord
    {
        [JsonPropertyName("Key")]
        public string Key { get; set; } = "";
        [JsonPropertyName("Status")]
        public HistoryRunStatus Status { get; set; }
        [JsonPropertyName("DurationMs")]
        public long? DurationMs { get; set; }
        [JsonPropertyName("ErrorMessage")]
        public string? ErrorMessage { get; set; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<HistoryService.HistoryRunMetaRecord>))]
internal partial class HistoryJsonContext : JsonSerializerContext
{
}
