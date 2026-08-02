using JustyBase.Models;
using Avalonia.Threading;

namespace JustyBase.Services.DataGrid;

public sealed class ResultGridSearchService : IResultGridSearchService
{
    private DispatcherTimer? _searchTimer;
    private Action? _pendingSearchCallback;
    private const int SearchDelayMs = 50;

    public int ApplySearch(
        TableOfSqlResults resultsTable,
        string? searchText,
        Dictionary<int, AditionalOneFilter>? additionalValues,
        bool containsGeneralSearch)
    {
        if (resultsTable is null)
        {
            throw new ArgumentNullException(nameof(resultsTable));
        }

        if (resultsTable.Rows is null || resultsTable.Rows.Count == 0 || resultsTable.Headers.Count == 0)
        {
            resultsTable.FilteredRows?.Clear();
            resultsTable.RebuildRowIndexMap();
            return 0;
        }

        var sr = new SearchInRows(resultsTable, searchText ?? string.Empty, additionalValues ?? [], containsGeneralSearch);
        sr.SearchAll();

        return resultsTable.FilteredRows.Count;
    }

    public void ScheduleSearch(Action searchCallback)
    {
        _pendingSearchCallback = searchCallback;
        if (_searchTimer is null)
        {
            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SearchDelayMs)
            };
            _searchTimer.Tick += OnSearchTimerTick;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchTimerTick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        var callback = _pendingSearchCallback;
        _pendingSearchCallback = null;
        callback?.Invoke();
    }
}
