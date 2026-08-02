namespace JustyBase.Common.Models;

public sealed class HistoryEntry
{
    public required DateTime Date { get; set; }
    public required string Database { get; set; }
    public required string Connection { get; set; }
    public required string SQL { get; set; }

    /// <summary>In-memory / sidecar flag — not part of the legacy history.dat binary format.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Sidecar meta — not part of the legacy history.dat binary format.</summary>
    public HistoryRunStatus Status { get; set; } = HistoryRunStatus.Unknown;

    /// <summary>Sidecar meta — not part of the legacy history.dat binary format.</summary>
    public long? DurationMs { get; set; }

    /// <summary>Sidecar meta — not part of the legacy history.dat binary format.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime RunDateTime => Date;

    public string StatusText => Status switch
    {
        HistoryRunStatus.Success => "OK",
        HistoryRunStatus.Failed => "Failed",
        HistoryRunStatus.Cancelled => "Cancelled",
        HistoryRunStatus.Unknown => "Unknown",
        _ => "",
    };

    public string DurationText
    {
        get
        {
            if (DurationMs is not long ms)
            {
                return "";
            }

            if (ms < 1000)
            {
                return $"{ms} ms";
            }

            if (ms < 60_000)
            {
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ms / 1000.0:0.##} s");
            }

            var ts = TimeSpan.FromMilliseconds(ms);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
    }

    public string ErrorShort
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ErrorMessage))
            {
                return "";
            }

            var oneLine = ErrorMessage.ReplaceLineEndings(" ");
            return oneLine.Length <= 120 ? oneLine : oneLine[..120];
        }
    }

    public string SqlShort
    {
        get
        {
            var res = SQL.Length <= 150 ? SQL : SQL[..150];
            return res.ReplaceLineEndings(" ");
        }
    }

    public string FavoriteKey => $"{Date.ToBinary()}|{Connection}|{Database}|{SQL.GetHashCode(StringComparison.Ordinal)}";

    public bool FiltrerRow(
        string searchTxt,
        bool favoritesOnly = false,
        HistoryRunStatus? statusFilter = null,
        HistoryDurationPreset durationPreset = HistoryDurationPreset.All)
    {
        if (favoritesOnly && !IsFavorite)
        {
            return false;
        }

        if (statusFilter is HistoryRunStatus requiredStatus && Status != requiredStatus)
        {
            return false;
        }

        if (!MatchesDurationPreset(durationPreset))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(searchTxt))
        {
            return true;
        }

        return SQL.Contains(searchTxt, StringComparison.OrdinalIgnoreCase)
            || Connection.Contains(searchTxt, StringComparison.OrdinalIgnoreCase)
            || Database.Contains(searchTxt, StringComparison.OrdinalIgnoreCase)
            || Date.ToString("G").Contains(searchTxt, StringComparison.OrdinalIgnoreCase)
            || (ErrorMessage?.Contains(searchTxt, StringComparison.OrdinalIgnoreCase) == true)
            || StatusText.Contains(searchTxt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Backward-compatible overload used by older call sites.</summary>
    public bool FiltrerRow(string searchTxt) => FiltrerRow(searchTxt, favoritesOnly: false);

    public bool FiltrerRow(string searchTxt, bool favoritesOnly) =>
        FiltrerRow(searchTxt, favoritesOnly, statusFilter: null, durationPreset: HistoryDurationPreset.All);

    private bool MatchesDurationPreset(HistoryDurationPreset preset)
    {
        if (preset == HistoryDurationPreset.All)
        {
            return true;
        }

        // Legacy rows without duration never match a specific duration filter.
        if (DurationMs is not long ms)
        {
            return false;
        }

        return preset switch
        {
            HistoryDurationPreset.Under1s => ms < 1_000,
            HistoryDurationPreset.From1To10s => ms >= 1_000 && ms < 10_000,
            HistoryDurationPreset.From10sTo1min => ms >= 10_000 && ms < 60_000,
            HistoryDurationPreset.Over1min => ms >= 60_000,
            _ => true,
        };
    }
}
