using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.QuickOpen;

namespace JustyBase.ViewModels;

public sealed partial class QuickOpenEntryViewModel : ObservableObject
{
    public QuickOpenEntryViewModel(QuickOpenListEntry entry)
    {
        IsHeader = entry.IsHeader;
        HeaderText = entry.HeaderText ?? string.Empty;
        Hit = entry.Hit;
        if (entry.Hit is { } hit)
        {
            PrimaryText = hit.DisplayName;
            SecondaryText = hit.Kind == QuickOpenHitKind.Content && hit.LineNumber is int line
                ? $"{hit.DisplayPath}  :{line}"
                : hit.DisplayPath;
            Snippet = hit.Kind is QuickOpenHitKind.Content or QuickOpenHitKind.History
                ? hit.Snippet
                : null;
            IsContent = Snippet is not null;
            Query = hit.Query;
        }
        else
        {
            PrimaryText = string.Empty;
            SecondaryText = string.Empty;
        }
    }

    public bool IsHeader { get; }
    public string HeaderText { get; }
    public QuickOpenHit? Hit { get; }
    public string PrimaryText { get; }
    public string SecondaryText { get; }
    public string? Snippet { get; }
    public bool IsContent { get; }
    public string? Query { get; }
    public bool IsSelectable => !IsHeader && Hit is not null;
}

public sealed partial class QuickOpenViewModel : ObservableObject
{
    private const int DebounceMs = 350;

    private readonly QuickOpenSearchService _searchService;
    private readonly IReadOnlyList<QuickOpenCandidate> _candidates;
    private readonly TimeSpan _contentTimeout;
    private readonly Action _closeCancel;
    private readonly Action<QuickOpenHit> _closeAccept;
    private readonly Action<QuickOpenGotoTarget>? _gotoPosition;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? _symbolSearch;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? _actionSearch;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? _schemaSearch;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? _historySearch;
    private readonly bool _searchEverything;

    private CancellationTokenSource? _contentCts;
    private CancellationTokenSource? _symbolCts;
    private CancellationTokenSource? _actionCts;
    private CancellationTokenSource? _schemaCts;
    private CancellationTokenSource? _historyCts;
    private CancellationTokenSource? _debounceCts;
    private IReadOnlyList<QuickOpenHit> _nameHits = [];
    private IReadOnlyList<QuickOpenHit> _contentHits = [];
    private IReadOnlyList<QuickOpenHit> _symbolHits = [];
    private IReadOnlyList<QuickOpenHit> _actionHits = [];
    private IReadOnlyList<QuickOpenHit> _schemaHits = [];
    private IReadOnlyList<QuickOpenHit> _historyHits = [];

    public string WindowTitle => _searchEverything ? "Go to Everything" : "Quick Open";

    public string SearchPlaceholder => _searchEverything
        ? "Search actions, files, symbols, tables and query history  ( :42:7   >   #   %   @   ? )"
        : "Search SQL files  ( :42:7 go to line and column   ? help )";

    public QuickOpenViewModel(
        QuickOpenSearchService searchService,
        IReadOnlyList<QuickOpenCandidate> candidates,
        TimeSpan contentTimeout,
        Action closeCancel,
        Action<QuickOpenHit> closeAccept,
        string? initialQuery = null,
        Action<QuickOpenGotoTarget>? gotoPosition = null,
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? symbolSearch = null,
        bool searchEverything = false,
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? actionSearch = null,
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? schemaSearch = null,
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? historySearch = null)
    {
        _searchService = searchService;
        _candidates = candidates;
        _contentTimeout = contentTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : contentTimeout;
        _closeCancel = closeCancel;
        _closeAccept = closeAccept;
        _gotoPosition = gotoPosition;
        _symbolSearch = symbolSearch;
        _searchEverything = searchEverything;
        _actionSearch = actionSearch;
        _schemaSearch = schemaSearch;
        _historySearch = historySearch;
        ApplyFilter(string.Empty);
        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            Query = initialQuery;
        }
        else if (_searchEverything)
        {
            _ = ScheduleEverythingAsync(string.Empty);
        }
    }

    public ObservableCollection<QuickOpenEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial QuickOpenEntryViewModel? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial string HintText { get; set; } = "↑↓ navigate  ·  Enter open  ·  Esc close  ·  ? help";

    partial void OnQueryChanged(string value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed == "?" || trimmed.StartsWith(':'))
        {
            // VS Code prefixes: no name/content search, handled in RebuildList/Accept.
            ClearAllHits();
            CancelAllSearches();
            RebuildList();
            return;
        }

        if (trimmed.StartsWith('>'))
        {
            ClearAllHits();
            CancelAllSearches();
            _ = ScheduleActionSearchAsync(trimmed[1..]);
            RebuildList();
            return;
        }

        if (trimmed.StartsWith('#'))
        {
            ClearAllHits();
            CancelAllSearches();
            _ = ScheduleSchemaSearchAsync(trimmed[1..]);
            RebuildList();
            return;
        }

        if (trimmed.StartsWith('%'))
        {
            ClearAllHits();
            CancelAllSearches();
            _ = ScheduleHistorySearchAsync(trimmed[1..]);
            RebuildList();
            return;
        }

        if (trimmed.StartsWith('@'))
        {
            // Symbol search: SQL names from the workspace index only.
            ClearAllHits();
            CancelAllSearches();
            _ = ScheduleSymbolSearchAsync(trimmed[1..]);
            RebuildList();
            return;
        }

        if (!_searchEverything)
        {
            // Prefix searches are only refreshed by ScheduleEverythingAsync, so leaving a
            // prefix in plain Quick Open must clear their rows before the list is rebuilt.
            _actionHits = [];
            _schemaHits = [];
            _historyHits = [];
            CancelActionSearch();
            CancelSchemaSearch();
            CancelHistorySearch();
        }

        ApplyFilter(value);
        if (_searchEverything)
        {
            _ = ScheduleEverythingAsync(trimmed);
        }
        else
        {
            _ = ScheduleContentSearchAsync(value);
            _ = ScheduleSymbolSearchAsync(value);
        }
    }

    private void ClearAllHits()
    {
        _nameHits = [];
        _contentHits = [];
        _symbolHits = [];
        _actionHits = [];
        _schemaHits = [];
        _historyHits = [];
    }

    private void CancelAllSearches()
    {
        CancelDebounce();
        CancelContentSearch();
        CancelSymbolSearch();
        CancelActionSearch();
        CancelSchemaSearch();
        CancelHistorySearch();
    }

    private void CancelDebounce()
    {
        try
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // ignore dispose races
        }
        finally
        {
            _debounceCts = null;
        }
    }

    [RelayCommand]
    public void MoveSelection(int delta)
    {
        var selectable = Entries.Where(e => e.IsSelectable).ToList();
        if (selectable.Count == 0)
            return;

        int current = SelectedEntry is null ? -1 : selectable.IndexOf(SelectedEntry);
        if (current < 0)
            current = 0;
        else
            current = (current + delta + selectable.Count) % selectable.Count;

        SelectedEntry = selectable[current];
    }

    [RelayCommand]
    public void AcceptSelection()
    {
        // VS Code ":42" or ":42:7" goes to that line (and column) in the active document.
        if (_gotoPosition is not null && TryParseGotoTarget(Query, out QuickOpenGotoTarget target))
        {
            CancelAllSearches();
            var navigate = _gotoPosition;
            _closeCancel();
            navigate(target);
            return;
        }

        var hit = SelectedEntry?.Hit;
        if (hit is null)
            hit = Entries.FirstOrDefault(e => e.IsSelectable)?.Hit;

        if (hit is null)
            return;

        CancelAllSearches();
        _closeAccept(hit);
    }

    [RelayCommand]
    public void Cancel()
    {
        CancelSearches();
        _closeCancel();
    }

    /// <summary>Stops every in-flight search without closing the window (used by OnClosing).</summary>
    public void CancelSearches() => CancelAllSearches();

    public void SelectEntryFromClick(QuickOpenEntryViewModel? entry)
    {
        if (entry is null || !entry.IsSelectable)
            return;
        SelectedEntry = entry;
    }

    public void AcceptEntryFromDoubleClick(QuickOpenEntryViewModel? entry)
    {
        if (entry is null || !entry.IsSelectable)
            return;
        SelectedEntry = entry;
        AcceptSelection();
    }

    private void ApplyFilter(string query)
    {
        _nameHits = _searchService.SearchByName(_candidates, query);
        RebuildList();
    }

    private async Task ScheduleContentSearchAsync(string query)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var debounce = new CancellationTokenSource();
        _debounceCts = debounce;

        string trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            _contentHits = [];
            RebuildList();
            return;
        }

        try
        {
            await Task.Delay(DebounceMs, debounce.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (debounce.IsCancellationRequested)
        {
            return;
        }

        await RunContentSearchAsync(trimmed).ConfigureAwait(true);
    }

    private async Task RunContentSearchAsync(string query)
    {
        CancelContentSearch();
        var cts = new CancellationTokenSource();
        _contentCts = cts;
        try
        {
            var hits = await _searchService.SearchByContentAsync(
                _candidates,
                query,
                _contentTimeout,
                cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
                return;

            if (!string.Equals(Query.Trim(), query, StringComparison.Ordinal))
                return;

            _contentHits = hits;
            RebuildList();
        }
        catch (OperationCanceledException)
        {
            // expected on debounce / close
        }
    }

    private void CancelContentSearch()
    {
        try
        {
            _contentCts?.Cancel();
            _contentCts?.Dispose();
        }
        catch
        {
            // ignore dispose races
        }
        finally
        {
            _contentCts = null;
        }
    }

    private async Task ScheduleSymbolSearchAsync(string query)
    {
        _symbolCts?.Cancel();
        _symbolCts?.Dispose();
        var cts = new CancellationTokenSource();
        _symbolCts = cts;

        string trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || _symbolSearch is null)
        {
            _symbolHits = [];
            RebuildList();
            return;
        }

        try
        {
            await Task.Delay(200, cts.Token).ConfigureAwait(true);
            IReadOnlyList<QuickOpenHit> hits = await _symbolSearch(trimmed, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _symbolHits = hits;
            RebuildList();
        }
        catch (OperationCanceledException)
        {
            // expected on debounce / close
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _symbolHits = [];
            RebuildList();
        }
    }

    private void CancelSymbolSearch()
    {
        try
        {
            _symbolCts?.Cancel();
            _symbolCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // ignore dispose races
        }
        finally
        {
            _symbolCts = null;
        }
    }

    private async Task ScheduleActionSearchAsync(string query)
    {
        _actionCts?.Cancel();
        _actionCts?.Dispose();
        var cts = new CancellationTokenSource();
        _actionCts = cts;

        IReadOnlyList<QuickOpenHit>? hits = await RunProviderAsync(
            _actionSearch, query?.Trim() ?? string.Empty, cts, 120).ConfigureAwait(true);
        if (hits is null)
        {
            return;
        }

        _actionHits = hits;
        RebuildList();
    }

    private async Task ScheduleSchemaSearchAsync(string query)
    {
        _schemaCts?.Cancel();
        _schemaCts?.Dispose();
        var cts = new CancellationTokenSource();
        _schemaCts = cts;

        IReadOnlyList<QuickOpenHit>? hits = await RunProviderAsync(
            _schemaSearch, query?.Trim() ?? string.Empty, cts, 160).ConfigureAwait(true);
        if (hits is null)
        {
            return;
        }

        _schemaHits = hits;
        RebuildList();
    }

    private async Task ScheduleHistorySearchAsync(string query)
    {
        _historyCts?.Cancel();
        _historyCts?.Dispose();
        var cts = new CancellationTokenSource();
        _historyCts = cts;

        IReadOnlyList<QuickOpenHit>? hits = await RunProviderAsync(
            _historySearch, query?.Trim() ?? string.Empty, cts, 120).ConfigureAwait(true);
        if (hits is null)
        {
            return;
        }

        _historyHits = hits;
        RebuildList();
    }

    private static async Task<IReadOnlyList<QuickOpenHit>?> RunProviderAsync(
        Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? provider,
        string query,
        CancellationTokenSource cts,
        int debounceMs)
    {
        if (provider is null)
        {
            return [];
        }

        try
        {
            if (debounceMs > 0)
            {
                await Task.Delay(debounceMs, cts.Token).ConfigureAwait(true);
            }

            IReadOnlyList<QuickOpenHit> hits = await provider(query, cts.Token).ConfigureAwait(true);
            return cts.IsCancellationRequested ? null : hits;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }
    }

    private Task ScheduleEverythingAsync(string query)
    {
        var searches = new List<Task>(5)
        {
            ScheduleActionSearchAsync(query),
            ScheduleSchemaSearchAsync(query),
            ScheduleHistorySearchAsync(query),
        };

        if (query.Length > 0)
        {
            searches.Add(ScheduleSymbolSearchAsync(query));
            searches.Add(ScheduleContentSearchAsync(query));
        }
        else
        {
            _symbolHits = [];
            _contentHits = [];
        }

        return Task.WhenAll(searches);
    }

    private void CancelActionSearch() => CancelProvider(ref _actionCts);

    private void CancelSchemaSearch() => CancelProvider(ref _schemaCts);

    private void CancelHistorySearch() => CancelProvider(ref _historyCts);

    private static void CancelProvider(ref CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
            source?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // ignore dispose races
        }
        finally
        {
            source = null;
        }
    }

    private void RebuildList()
    {
        string trimmed = Query?.Trim() ?? string.Empty;
        if (trimmed == "?")
        {
            Entries.Clear();
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, "Type to search files, symbols, actions, tables and history", null)));
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, ":42:7 - go to line 42, column 7 in the active file", null)));
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, "> - find an action (command palette)", null)));
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, "# - find a table, view or column", null)));
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, "% - find a recent query", null)));
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, "@ - find a SQL symbol in the workspace", null)));
            Entries.Add(new QuickOpenEntryViewModel(new QuickOpenListEntry(true, "letters match in order (fuzzy), open files first", null)));
            SelectedEntry = null;
            HintText = "Esc close";
            return;
        }

        if (trimmed.StartsWith(':'))
        {
            Entries.Clear();
            SelectedEntry = null;
            HintText = TryParseGotoTarget(trimmed, out _)
                ? "Enter go to line and column  ·  Esc close"
                : "Type a line or line:column, e.g. :42 or :42:7  ·  Esc close";
            return;
        }

        var previousHitKey = SelectedEntry?.Hit is { } prev
            ? HitKey(prev)
            : null;

        var list = QuickOpenSearchService.BuildList(
            _nameHits,
            _contentHits,
            _symbolHits,
            _actionHits,
            _schemaHits,
            _historyHits);
        Entries.Clear();
        foreach (var entry in list)
            Entries.Add(new QuickOpenEntryViewModel(entry));

        var selectable = Entries.Where(e => e.IsSelectable).ToList();
        if (selectable.Count == 0)
        {
            SelectedEntry = null;
            HintText = _searchEverything
                ? "No matches"
                : _candidates.Count == 0
                    ? "No SQL files in Files / Git / open editors"
                    : "No matches";
            return;
        }

        QuickOpenEntryViewModel? restore = null;
        if (previousHitKey is not null)
        {
            restore = selectable.FirstOrDefault(e => e.Hit is { } h && HitKey(h) == previousHitKey);
        }

        SelectedEntry = restore ?? selectable[0];
        HintText = $"{selectable.Count} results  ·  ↑↓ navigate  ·  Enter open  ·  Esc close";
    }

    private static bool TryParseGotoTarget(string? query, out QuickOpenGotoTarget target)
    {
        target = default;
        string trimmed = query?.Trim() ?? string.Empty;
        if (!trimmed.StartsWith(':') || trimmed.Length < 2)
        {
            return false;
        }

        string body = trimmed[1..].Trim();
        int separator = body.IndexOfAny([':', ',']);
        if (separator < 0)
        {
            if (int.TryParse(body, out int line) && line > 0)
            {
                target = new QuickOpenGotoTarget(line, 1);
                return true;
            }

            return false;
        }

        string lineText = body[..separator].Trim();
        string columnText = body[(separator + 1)..].Trim();
        if (!int.TryParse(lineText, out int parsedLine)
            || !int.TryParse(columnText, out int parsedColumn)
            || parsedLine <= 0
            || parsedColumn <= 0)
        {
            return false;
        }

        target = new QuickOpenGotoTarget(parsedLine, parsedColumn);
        return true;
    }

    private static string HitKey(QuickOpenHit hit)
        => $"{hit.Kind}|{hit.FilePath}|{hit.DocumentId}|{hit.LineNumber}|{hit.MatchIndex}|{hit.ActionId}|{hit.DisplayPath}|{hit.InsertText}|{hit.DisplayName}";
}
