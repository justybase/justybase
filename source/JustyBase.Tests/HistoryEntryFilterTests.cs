using JustyBase.Common.Models;

namespace JustyBase.Tests;

public class HistoryEntryFilterTests
{
    [Theory]
    [InlineData("select", true)]
    [InlineData("PROD", true)]
    [InlineData("analytics", true)]
    [InlineData("missing", false)]
    public void FiltrerRow_MatchesSqlConnectionOrDatabase(string search, bool expected)
    {
        var entry = new HistoryEntry
        {
            Date = new DateTime(2026, 1, 2, 3, 4, 5),
            Connection = "PROD",
            Database = "analytics",
            SQL = "SELECT 1 FROM dual"
        };

        Assert.Equal(expected, entry.FiltrerRow(search));
    }

    [Fact]
    public void FiltrerRow_FavoritesOnly_ExcludesNonFavorites()
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "select 1",
            IsFavorite = false
        };

        Assert.False(entry.FiltrerRow("", favoritesOnly: true));
        entry.IsFavorite = true;
        Assert.True(entry.FiltrerRow("", favoritesOnly: true));
    }

    [Fact]
    public void FiltrerRow_EmptySearch_ReturnsTrue()
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "x"
        };
        Assert.True(entry.FiltrerRow(""));
        Assert.True(entry.FiltrerRow("   "));
    }

    [Fact]
    public void FiltrerRow_StatusFilter_MatchesRequiredStatus()
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "select 1",
            Status = HistoryRunStatus.Failed,
            ErrorMessage = "relation does not exist",
        };

        Assert.True(entry.FiltrerRow("", statusFilter: HistoryRunStatus.Failed));
        Assert.False(entry.FiltrerRow("", statusFilter: HistoryRunStatus.Success));
        Assert.True(entry.FiltrerRow("", statusFilter: null));
        Assert.False(entry.FiltrerRow("", statusFilter: HistoryRunStatus.Unknown));
        Assert.True(entry.FiltrerRow("relation"));
    }

    [Fact]
    public void FiltrerRow_UnknownStatusFilter_MatchesLegacyOnly()
    {
        var legacy = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "select 1",
            Status = HistoryRunStatus.Unknown,
        };
        var ok = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "select 2",
            Status = HistoryRunStatus.Success,
        };

        Assert.True(legacy.FiltrerRow("", statusFilter: HistoryRunStatus.Unknown));
        Assert.False(ok.FiltrerRow("", statusFilter: HistoryRunStatus.Unknown));
    }

    [Theory]
    [InlineData(500, HistoryDurationPreset.Under1s, true)]
    [InlineData(500, HistoryDurationPreset.From1To10s, false)]
    [InlineData(2500, HistoryDurationPreset.From1To10s, true)]
    [InlineData(15_000, HistoryDurationPreset.From10sTo1min, true)]
    [InlineData(120_000, HistoryDurationPreset.Over1min, true)]
    [InlineData(120_000, HistoryDurationPreset.Under1s, false)]
    public void FiltrerRow_DurationPreset_MatchesRanges(long durationMs, HistoryDurationPreset preset, bool expected)
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "select 1",
            DurationMs = durationMs,
            Status = HistoryRunStatus.Success,
        };

        Assert.Equal(expected, entry.FiltrerRow("", durationPreset: preset));
        Assert.True(entry.FiltrerRow("", durationPreset: HistoryDurationPreset.All));
    }

    [Fact]
    public void FiltrerRow_DurationPreset_ExcludesLegacyRowsWithoutDuration()
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "select 1",
        };

        Assert.False(entry.FiltrerRow("", durationPreset: HistoryDurationPreset.Under1s));
        Assert.True(entry.FiltrerRow("", durationPreset: HistoryDurationPreset.All));
    }

    [Theory]
    [InlineData(500, "500 ms")]
    [InlineData(1500, "1.5 s")]
    [InlineData(65_000, "1:05")]
    public void DurationText_FormatsReasonably(long ms, string expected)
    {
        var entry = new HistoryEntry
        {
            Date = DateTime.UtcNow,
            Connection = "c",
            Database = "d",
            SQL = "x",
            DurationMs = ms,
        };

        Assert.Equal(expected, entry.DurationText);
    }
}
