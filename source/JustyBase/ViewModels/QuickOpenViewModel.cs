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
            Snippet = hit.Kind == QuickOpenHitKind.Content ? hit.Snippet : null;
            IsContent = hit.Kind == QuickOpenHitKind.Content;
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

    private CancellationTokenSource? _contentCts;
    private CancellationTokenSource? _debounceCts;
    private IReadOnlyList<QuickOpenHit> _nameHits = [];
    private IReadOnlyList<QuickOpenHit> _contentHits = [];

    public QuickOpenViewModel(
        QuickOpenSearchService searchService,
        IReadOnlyList<QuickOpenCandidate> candidates,
        TimeSpan contentTimeout,
        Action closeCancel,
        Action<QuickOpenHit> closeAccept)
    {
        _searchService = searchService;
        _candidates = candidates;
        _contentTimeout = contentTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : contentTimeout;
        _closeCancel = closeCancel;
        _closeAccept = closeAccept;
        ApplyFilter(string.Empty);
    }

    public ObservableCollection<QuickOpenEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial QuickOpenEntryViewModel? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial string HintText { get; set; } = "↑↓ navigate  ·  Enter open  ·  Esc close";

    partial void OnQueryChanged(string value)
    {
        ApplyFilter(value);
        _ = ScheduleContentSearchAsync(value);
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
        var hit = SelectedEntry?.Hit;
        if (hit is null)
            hit = Entries.FirstOrDefault(e => e.IsSelectable)?.Hit;

        if (hit is null)
            return;

        CancelContentSearch();
        _closeAccept(hit);
    }

    [RelayCommand]
    public void Cancel()
    {
        CancelContentSearch();
        _closeCancel();
    }

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

    private void RebuildList()
    {
        var previousHitKey = SelectedEntry?.Hit is { } prev
            ? HitKey(prev)
            : null;

        var list = QuickOpenSearchService.BuildList(_nameHits, _contentHits);
        Entries.Clear();
        foreach (var entry in list)
            Entries.Add(new QuickOpenEntryViewModel(entry));

        var selectable = Entries.Where(e => e.IsSelectable).ToList();
        if (selectable.Count == 0)
        {
            SelectedEntry = null;
            HintText = _candidates.Count == 0
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

    private static string HitKey(QuickOpenHit hit)
        => $"{hit.Kind}|{hit.FilePath}|{hit.DocumentId}|{hit.LineNumber}|{hit.MatchIndex}";
}
