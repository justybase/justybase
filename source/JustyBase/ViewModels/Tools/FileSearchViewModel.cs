using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Services;
using JustyBase.Views.OtherDialogs;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace JustyBase.ViewModels.Tools;

/// <summary>
/// VS Code style File Search panel. Separate dock tab from Explorer.
/// Search roots come from <see cref="SearchPaths"/> (semicolon separated).
/// </summary>
public sealed partial class FileSearchViewModel : Tool, IDisposable
{
    private readonly IContentSearchService _searchService;
    private readonly IGeneralApplicationData _applicationData;
    private readonly IAvaloniaSpecificHelpers _avaloniaHelpers;
    private readonly IMessageForUserTools _messageForUserTools;
    private CancellationTokenSource? _searchCancellation;
    private ContentSearchOptions? _lastOptions;
    private string[]? _lastRoots;
    private int _searchVersion;
    private int _currentHitIndex = -1;

    public FileSearchViewModel(IFactory factory, IContentSearchService searchService,
        IGeneralApplicationData applicationData, IAvaloniaSpecificHelpers avaloniaHelpers,
        IMessageForUserTools messageForUserTools)
    {
        this.Factory = factory;
        _searchService = searchService;
        _applicationData = applicationData;
        _avaloniaHelpers = avaloniaHelpers;
        _messageForUserTools = messageForUserTools;
        Pattern = "";
        Replacement = "";
        Include = "";
        Exclude = "";
        SearchPaths = applicationData.Config.StartsFolderPaths is { Count: > 0 } paths
            ? string.Join(";", paths)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Status = "Enter text to search in files";
        SkippedDetails = "";
    }

    public ObservableCollection<ContentSearchFileGroup> Results { get; } = [];

    [ObservableProperty]
    public partial string Pattern { get; set; }

    [ObservableProperty]
    public partial string Replacement { get; set; }

    [ObservableProperty]
    public partial string Include { get; set; }

    [ObservableProperty]
    public partial string Exclude { get; set; }

    [ObservableProperty]
    public partial string SearchPaths { get; set; }

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    [ObservableProperty]
    public partial bool WholeWords { get; set; }

    [ObservableProperty]
    public partial bool UseRegex { get; set; }

    [ObservableProperty]
    public partial bool SearchInSqlComments { get; set; }

    [ObservableProperty]
    public partial bool SearchInProgress { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial string SkippedDetails { get; set; }

    [ObservableProperty]
    public partial bool IsReplaceExpanded { get; set; }

    [ObservableProperty]
    public partial bool ShowExcludeOptions { get; set; }

    /// <summary>
    /// Incremented to ask the view for keyboard focus in the search box
    /// (e.g. after Ctrl+Shift+F). The view observes this property.
    /// </summary>
    [ObservableProperty]
    public partial int FocusSearchRequest { get; set; }

    public void RequestSearchFocus() => FocusSearchRequest++;

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var version = ++_searchVersion;
        Results.Clear();
        _currentHitIndex = -1;
        SkippedDetails = "";
        if (string.IsNullOrEmpty(Pattern))
        {
            Status = "Enter text to search in files";
            cancellation.Dispose();
            _searchCancellation = null;
            return;
        }

        var options = CurrentOptions();
        try
        {
            SearchInProgress = true;
            Status = "Searching…";
            var roots = GetRoots();
            if (roots.Length == 0)
            {
                Status = "Choose a folder to search";
                return;
            }
            var run = await Task.Run(() => _searchService.SearchAsync(roots, options, cancellation.Token));
            if (version != _searchVersion)
            {
                return;
            }
            _lastOptions = options;
            _lastRoots = roots;
            foreach (var file in run.Files)
            {
                Results.Add(new ContentSearchFileGroup(file).Initialize());
            }
            SkippedDetails = string.Join(Environment.NewLine, run.Skipped);
            Status = $"{run.Files.Sum(x => x.Hits.Count)} results in {run.Files.Count} files" +
                (run.Skipped.Count > 0 ? $" · {run.Skipped.Count} skipped (hover for details)" : "");
        }
        catch (OperationCanceledException)
        {
            if (version == _searchVersion)
            {
                Status = "Search cancelled";
            }
        }
        catch (RegexParseException ex)
        {
            if (version == _searchVersion)
            {
                Status = $"Invalid regular expression: {ex.Message}";
            }
        }
        catch (RegexMatchTimeoutException)
        {
            if (version == _searchVersion)
            {
                Status = "Regular expression timed out";
            }
        }
        finally
        {
            if (version == _searchVersion)
            {
                SearchInProgress = false;
                _searchCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CancelSearch() => _searchCancellation?.Cancel();

    [RelayCommand]
    private void ToggleReplace() => IsReplaceExpanded = !IsReplaceExpanded;

    [RelayCommand]
    private void ClearPattern()
    {
        Pattern = "";
        _searchCancellation?.Cancel();
    }

    [RelayCommand]
    private void ClearResults()
    {
        _searchCancellation?.Cancel();
        Results.Clear();
        _currentHitIndex = -1;
        SkippedDetails = "";
        _lastOptions = null;
        _lastRoots = null;
        Status = "Enter text to search in files";
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in Results)
        {
            group.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void DismissGroup(ContentSearchFileGroup? group)
    {
        if (group is not null)
        {
            Results.Remove(group);
        }
    }

    [RelayCommand]
    private async Task CopyPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var clipboard = _avaloniaHelpers.GetClipboard();
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(path);
        }
    }

    [RelayCommand]
    private void OpenPathInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _messageForUserTools.ShowOrShowInExplorerHelper(path);
    }

    [RelayCommand]
    private void NextHit() => StepHit(1);

    [RelayCommand]
    private void PreviousHit() => StepHit(-1);

    [RelayCommand]
    private async Task BrowseForFolderAsync()
    {
        var folders = await _avaloniaHelpers.GetStorageProvider().OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false, Title = "Choose a folder to search" });
        if (folders is null || folders.Count == 0)
        {
            return;
        }
        var selected = folders[0].Path.LocalPath;
        var roots = GetRoots().ToList();
        if (!roots.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(selected);
        }
        SearchPaths = string.Join(";", roots);
    }

    private void StepHit(int delta)
    {
        var all = Results.SelectMany(static group => group.Hits).ToArray();
        if (all.Length == 0)
        {
            return;
        }

        _currentHitIndex = ((_currentHitIndex + delta) % all.Length + all.Length) % all.Length;
        OpenHit(all[_currentHitIndex]);
    }

    public void OpenHit(ContentSearchHitRow row)
    {
        var documents = (IActiveDocumentManager)Factory;
        var file = row.Group.File;
        if (!File.Exists(file.Path))
        {
            Status = "File no longer exists";
            return;
        }
        if (!_searchService.IsUnchanged(file))
        {
            Status = "File changed since the search. Search again to update line numbers";
            return;
        }
        documents.AddNewDocumentFromFile([file.Path]);
        var remaining = 12;
        void SelectWhenReady()
        {
            var document = documents.FindOpenSqlDocument(null, file.Path);
            var editor = document?.SqlEditor;
            if (editor?.Document is null || editor.Document.TextLength < row.Hit.Offset + row.Hit.Length)
            {
                if (--remaining > 0)
                {
                    DispatcherTimer.RunOnce(SelectWhenReady, TimeSpan.FromMilliseconds(60));
                }
                return;
            }
            documents.FocusSqlDocument(document!);
            editor.Select(row.Hit.Offset, row.Hit.Length);
            editor.TextArea.Caret.BringCaretToView();
            editor.Focus();
        }
        DispatcherTimer.RunOnce(SelectWhenReady, TimeSpan.FromMilliseconds(60));
    }

    public async Task ReplaceAsync(ContentSearchHitRow? hit = null, ContentSearchFileGroup? group = null)
    {
        var documents = (IActiveDocumentManager)Factory;
        if (!IsReplaceExpanded)
        {
            Status = "Expand Replace to preview replacements";
            return;
        }
        if (_lastOptions is null || _lastRoots is null || Results.Count == 0)
        {
            return;
        }
        if (!_lastRoots.SequenceEqual(GetRoots(), StringComparer.OrdinalIgnoreCase))
        {
            Status = "Search again after changing the folders";
            return;
        }
        if (CurrentOptions() != _lastOptions)
        {
            Status = "Search again after changing search options";
            return;
        }

        IEnumerable<ContentSearchFileGroup> source = group is not null ? [group] :
            hit is not null ? [hit.Group] : Results.ToArray();
        var changes = new List<ContentReplacement>();
        var skipped = new List<string>();
        foreach (var item in source)
        {
            var file = item.File;
            if (!_searchService.IsUnchanged(file))
            {
                skipped.Add($"{file.RelativePath}: changed on disk");
                continue;
            }
            var open = documents.FindOpenSqlDocument(null, file.Path);
            if (open?.SqlEditor is { } editor && !string.Equals(editor.Text, file.Text, StringComparison.Ordinal))
            {
                skipped.Add($"{file.RelativePath}: unsaved editor changes");
                continue;
            }
            try
            {
                var preview = _searchService.Preview(file, _lastOptions, Replacement,
                    hit is null ? null : [hit.Hit.Offset]);
                if (preview is not null)
                {
                    changes.Add(preview);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                Status = $"Invalid replacement: {ex.Message}";
                return;
            }
        }
        if (changes.Count == 0)
        {
            Status = skipped.Count == 0 ? "No changes to preview" : string.Join("; ", skipped);
            return;
        }

        var previewWindow = new ReplacePreviewWindow(changes, skipped);
        var approved = await previewWindow.ShowDialog<bool>(_avaloniaHelpers.GetMainWindow());
        if (!approved)
        {
            return;
        }

        var changed = 0;
        foreach (var replacement in changes)
        {
            try
            {
                _searchService.Apply(replacement);
                changed += replacement.Count;
                var open = documents.FindOpenSqlDocument(null, replacement.File.Path);
                if (open?.SqlEditor is { } editor)
                {
                    editor.Text = replacement.NewText;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add($"{replacement.File.RelativePath}: {ex.Message}");
            }
        }
        await SearchAsync();
        Status = skipped.Count == 0 ? $"Replaced {changed} occurrences" :
            $"Replaced {changed} occurrences. Skipped: {string.Join("; ", skipped)}";
    }

    private ContentSearchOptions CurrentOptions() => new(Pattern, MatchCase, WholeWords, UseRegex,
        SearchInSqlComments, Include, Exclude);

    private string[] GetRoots() => (SearchPaths ?? "").Split(';',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
