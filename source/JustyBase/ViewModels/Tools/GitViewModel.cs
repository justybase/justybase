using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Services;
using JustyBase.Services.Git;

namespace JustyBase.ViewModels.Tools;

public sealed partial class GitViewModel : Tool, IDisposable
{
    private readonly IGitService _gitService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IActiveDocumentManager _activeDocumentManager;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IGitDiffPresentationService _gitDiffPresentation;
    private readonly IGitCommitMessageAiService _commitMessageAi;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private string? _manualRepoPath;
    private int _refreshVersion;
    private int _previewVersion;
    private int _commitFilesVersion;
    private string? _selectedCommitHash;

    public GitViewModel(
        IGitService gitService,
        IGeneralApplicationData generalApplicationData,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IActiveDocumentManager activeDocumentManager,
        IMessageForUserTools messageForUserTools,
        IGitDiffPresentationService gitDiffPresentation,
        IGitCommitMessageAiService commitMessageAi)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _generalApplicationData = generalApplicationData ?? throw new ArgumentNullException(nameof(generalApplicationData));
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers ?? throw new ArgumentNullException(nameof(avaloniaSpecificHelpers));
        _activeDocumentManager = activeDocumentManager ?? throw new ArgumentNullException(nameof(activeDocumentManager));
        _messageForUserTools = messageForUserTools ?? throw new ArgumentNullException(nameof(messageForUserTools));
        _gitDiffPresentation = gitDiffPresentation ?? throw new ArgumentNullException(nameof(gitDiffPresentation));
        _commitMessageAi = commitMessageAi ?? throw new ArgumentNullException(nameof(commitMessageAi));
    }

    public ObservableCollection<string> AvailableRepos { get; } = [];
    public ObservableCollection<GitCommitItem> Commits { get; } = [];
    public ObservableCollection<GitCommitItem> Timeline { get; } = [];
    public ObservableCollection<string> Branches { get; } = [];
    public ObservableCollection<GitCommitFileItem> CommitFiles { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GitFileStatusItem> StagedChanges { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GitFileStatusItem> UnstagedChanges { get; private set; } = [];

    public int StagedCount => StagedChanges.Count;
    public int UnstagedCount => UnstagedChanges.Count;
    public bool HasStagedChanges => StagedCount > 0;
    public bool HasUnstagedChanges => UnstagedCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepository))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StageAllAndCommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StageAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(PullCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(PromptCreateBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveLocalIdentityCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommitMessageCommand))]
    public partial string? SelectedRepoPath { get; set; }

    private bool _suppressRepoChange;

    partial void OnSelectedRepoPathChanged(string? value)
    {
        if (_suppressRepoChange || string.IsNullOrWhiteSpace(value) || _disposed)
            return;
        _ = RefreshAsync();
    }

    public bool HasRepository => !string.IsNullOrWhiteSpace(SelectedRepoPath);
    public bool ShowEmptyState => !IsBusy && (!IsGitAvailable || !HasRepository);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsGitAvailable { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRepositoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StageAllAndCommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StageAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(PullCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(PromptCreateBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveLocalIdentityCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommitMessageCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StageAllAndCommitCommand))]
    public partial string CommitMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommitMessageCommand))]
    public partial bool IsGeneratingCommitMessage { get; private set; }

    public bool CanShowGenerateCommitMessage => _commitMessageAi.IsAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchDisplay))]
    public partial string BranchName { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchDisplay))]
    public partial bool IsDetached { get; private set; }

    /// <summary>Branch label like VS Code status bar, e.g. main* when dirty.</summary>
    public string BranchDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BranchName))
                return string.Empty;

            string name = IsDetached ? $"detached ({BranchName})" : BranchName;
            return HasUncommittedChanges ? $"{name}*" : name;
        }
    }

    public bool HasUncommittedChanges => StagedCount + UnstagedCount > 0;

    [ObservableProperty]
    public partial string? ActiveFilePath { get; private set; }

    [ObservableProperty]
    public partial string? TimelineFileName { get; private set; }

    public bool HasTimeline => Timeline.Count > 0;

    [ObservableProperty]
    public partial string LocalUserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LocalUserEmail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? IdentitySummary { get; private set; }

    [ObservableProperty]
    public partial GitFileStatusItem? SelectedStagedItem { get; set; }

    [ObservableProperty]
    public partial GitFileStatusItem? SelectedUnstagedItem { get; set; }

    partial void OnSelectedStagedItemChanged(GitFileStatusItem? value)
    {
        if (value is not null)
            _ = PreviewDiffCommand.ExecuteAsync(value);
    }

    partial void OnSelectedUnstagedItemChanged(GitFileStatusItem? value)
    {
        if (value is not null)
            _ = PreviewDiffCommand.ExecuteAsync(value);
    }

    [ObservableProperty]
    public partial GitCommitItem? SelectedCommit { get; set; }

    [ObservableProperty]
    public partial GitCommitFileItem? SelectedCommitFile { get; set; }

    [ObservableProperty]
    public partial object? SelectedHistoryNode { get; set; }

    [ObservableProperty]
    public partial GitCommitItem? SelectedTimelineCommit { get; set; }

    partial void OnSelectedTimelineCommitChanged(GitCommitItem? value)
    {
        if (value is not null)
            _ = PreviewTimelineCommitCommand.ExecuteAsync(value);
    }

    partial void OnStagedChangesChanged(IReadOnlyList<GitFileStatusItem> value)
    {
        OnPropertyChanged(nameof(StagedCount));
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(HasUncommittedChanges));
        OnPropertyChanged(nameof(BranchDisplay));
        GenerateCommitMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnUnstagedChangesChanged(IReadOnlyList<GitFileStatusItem> value)
    {
        OnPropertyChanged(nameof(UnstagedCount));
        OnPropertyChanged(nameof(HasUnstagedChanges));
        OnPropertyChanged(nameof(HasUncommittedChanges));
        OnPropertyChanged(nameof(BranchDisplay));
        GenerateCommitMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCommitChanged(GitCommitItem? value)
    {
        if (value is not null)
            _ = LoadCommitFilesAsync(value);
    }

    partial void OnSelectedHistoryNodeChanged(object? value)
    {
        switch (value)
        {
            case GitCommitItem commit:
                SelectedCommit = commit;
                break;
            case GitCommitFileItem file:
                SelectedCommitFile = file;
                if (!string.IsNullOrWhiteSpace(file.CommitHash))
                    _selectedCommitHash = file.CommitHash;
                _ = PreviewCommitFileCommand.ExecuteAsync(file);
                break;
        }
    }

    public Task EnsureCommitTooltipAsync(GitCommitItem commit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return LoadCommitTooltipAsync(commit);
    }

    private async Task LoadCommitTooltipAsync(GitCommitItem commit)
    {
        if (commit.TooltipLoaded || string.IsNullOrWhiteSpace(SelectedRepoPath))
            return;

        try
        {
            GitCommitTooltipInfo info = await _gitService
                .GetCommitTooltipAsync(SelectedRepoPath, commit.Hash, _lifetime.Token)
                .ConfigureAwait(false);

            await OnUiAsync(() => commit.ApplyTooltip(info)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            await OnUiAsync(() => commit.ApplyTooltip(new GitCommitTooltipInfo(string.Empty, 0, 0, 0)))
                .ConfigureAwait(false);
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await DiscoverAndRefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    public void SetActiveFile(string? filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = SetActiveFileAsync(filePath);
    }

    private async Task SetActiveFileAsync(string? filePath)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string? normalized = NormalizeActiveFilePath(filePath);
            if (string.Equals(ActiveFilePath, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            await OnUiAsync(() =>
            {
                ActiveFilePath = normalized;
                TimelineFileName = ActiveFilePath is null ? null : Path.GetFileName(ActiveFilePath);
                SelectedTimelineCommit = null;
            }).ConfigureAwait(false);
            await RefreshTimelineAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    private static string? NormalizeActiveFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        try
        {
            return Path.GetFullPath(filePath.Trim());
        }
        catch
        {
            return filePath.Trim();
        }
    }

    public void SyncActiveDocumentFile()
    {
        string? path = _activeDocumentManager.ActiveSqlDocumentViewModel?.FilePath;
        SetActiveFile(path);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task Refresh() => DiscoverAndRefreshAsync();

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpenRepo))]
    private async Task OpenRepositoryAsync()
    {
        var storage = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storage is null)
            return;

        var folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false, Title = "Open Git repository" });
        if (folders.Count == 0)
            return;

        string? folder = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        string? repo = NormalizeRepoPath(_gitService.DiscoverRepo(folder));
        if (repo is null)
        {
            await ReportErrorAsync("Selected folder is not a Git repository (no .git found).").ConfigureAwait(false);
            return;
        }

        _manualRepoPath = repo;
        await DiscoverAndRefreshAsync().ConfigureAwait(false);
        await SelectRepoAsync(repo).ConfigureAwait(false);
    }

    private bool CanOpenRepo() => !IsBusy;

    [RelayCommand]
    private async Task SelectRepoAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        await OnUiAsync(() =>
        {
            _suppressRepoChange = true;
            try
            {
                SelectedRepoPath = NormalizeRepoPath(path) ?? path;
            }
            finally
            {
                _suppressRepoChange = false;
            }
        }).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (!CanCommit())
            return;

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .CommitAsync(SelectedRepoPath!, CommitMessage.Trim(), _lifetime.Token)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
                return;
            }

            await OnUiAsync(() => CommitMessage = string.Empty).ConfigureAwait(false);
            await SetStatusAsync("Commit created.").ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    private bool CanCommit() =>
        CanMutate()
        && !string.IsNullOrWhiteSpace(CommitMessage)
        && StagedCount > 0;

    [RelayCommand(CanExecute = nameof(CanStageAllAndCommit))]
    private async Task StageAllAndCommitAsync()
    {
        if (!CanStageAllAndCommit())
            return;

        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            await ReportErrorAsync("Enter a commit message first.").ConfigureAwait(false);
            return;
        }

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            if (StagedCount + UnstagedCount > 0)
            {
                GitCommandResult stage = await _gitService
                    .StageAllAsync(SelectedRepoPath!, _lifetime.Token)
                    .ConfigureAwait(false);
                if (!stage.Succeeded)
                {
                    await ReportErrorAsync(Truncate(stage.CombinedOutput)).ConfigureAwait(false);
                    return;
                }
            }

            GitCommandResult result = await _gitService
                .CommitAsync(SelectedRepoPath!, CommitMessage.Trim(), _lifetime.Token)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
                return;
            }

            await OnUiAsync(() => CommitMessage = string.Empty).ConfigureAwait(false);
            await SetStatusAsync("Staged all and committed.").ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    private bool CanStageAllAndCommit() =>
        CanMutate()
        && (StagedCount + UnstagedCount) > 0;

    [RelayCommand(CanExecute = nameof(CanGenerateCommitMessage))]
    private async Task GenerateCommitMessageAsync()
    {
        if (!CanGenerateCommitMessage())
            return;

        await OnUiAsync(() =>
        {
            IsGeneratingCommitMessage = true;
            ErrorMessage = null;
            StatusMessage = "Generating commit message…";
        }).ConfigureAwait(false);

        try
        {
            string context = await _gitService
                .GetWorkingTreeChangeSummaryAsync(SelectedRepoPath!, _lifetime.Token)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(context))
            {
                await ReportErrorAsync("No staged or unstaged changes to summarize.").ConfigureAwait(false);
                return;
            }

            string? message = await _commitMessageAi.GenerateAsync(context, _lifetime.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(message))
            {
                await ReportErrorAsync(
                    "Embedded AI returned an empty commit message. Enable Embedded FIM in Settings and ensure the model is downloaded.")
                    .ConfigureAwait(false);
                return;
            }

            await OnUiAsync(() =>
            {
                CommitMessage = message;
                StatusMessage = "Commit message generated.";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
        finally
        {
            await OnUiAsync(() => IsGeneratingCommitMessage = false).ConfigureAwait(false);
        }
    }

    private bool CanGenerateCommitMessage() =>
        !IsGeneratingCommitMessage
        && !IsBusy
        && _commitMessageAi.IsAvailable
        && IsGitAvailable
        && !string.IsNullOrWhiteSpace(SelectedRepoPath)
        && (StagedCount + UnstagedCount) > 0;

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task StageAllAsync()
    {
        if (!CanMutate() || (StagedCount + UnstagedCount) == 0)
            return;

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService.StageAllAsync(SelectedRepoPath!, _lifetime.Token).ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
                await RefreshStatusOnlyAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task StageSelectedAsync(GitFileStatusItem? item)
    {
        item ??= SelectedUnstagedItem;
        if (item is null || !CanMutate())
            return;

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .StageAsync(SelectedRepoPath!, [item.Path], _lifetime.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
                await RefreshStatusOnlyAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task UnstageSelectedAsync(GitFileStatusItem? item)
    {
        item ??= SelectedStagedItem;
        if (item is null || !CanMutate() || !item.IsStaged)
            return;

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .UnstageAsync(SelectedRepoPath!, [item.Path], _lifetime.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
                await RefreshStatusOnlyAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task DiscardSelectedAsync(GitFileStatusItem? item)
    {
        item ??= SelectedUnstagedItem ?? SelectedStagedItem;
        if (item is null || !CanMutate())
            return;

        bool confirmed = await _messageForUserTools.ShowConfirmationDialogAsync(
            item.Kind == GitChangeKind.Untracked
                ? $"Delete untracked file '{item.Path}'?"
                : $"Discard changes in '{item.Path}'?",
            "Confirm").ConfigureAwait(false);
        if (!confirmed)
            return;

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result;
            if (item.Kind == GitChangeKind.Untracked)
            {
                result = await _gitService
                    .DeleteUntrackedAsync(SelectedRepoPath!, [item.Path], _lifetime.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                result = await _gitService
                    .DiscardAsync(SelectedRepoPath!, [item.Path], _lifetime.Token)
                    .ConfigureAwait(false);
            }

            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
                await RefreshStatusOnlyAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task PreviewDiffAsync(GitFileStatusItem? item)
    {
        item ??= SelectedUnstagedItem ?? SelectedStagedItem;
        if (item is null || string.IsNullOrWhiteSpace(SelectedRepoPath))
            return;

        if (!IsDiffableTextPath(item.Path))
            return;

        int version = Interlocked.Increment(ref _previewVersion);
        string repo = SelectedRepoPath;

        try
        {
            GitFileStatus status = item == SelectedStagedItem && item.IsStaged && item.IsUnstaged
                ? item.AsStagedOnlyPreview().ToStatus()
                : item.ToStatus();

            if (IsOverMaxDiffSize(repo, item.Path))
                return;

            GitFileContents contents = await _gitService
                .GetFileContentsAsync(repo, status, _lifetime.Token)
                .ConfigureAwait(false);

            if (version != Volatile.Read(ref _previewVersion))
                return;

            if (IsOverMaxDiffSize(contents))
                return;

            await OnUiAsync(() =>
                _gitDiffPresentation.ShowGitDiff(contents.Title, contents.OldText, contents.NewText))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _previewVersion))
                await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task PreviewCommitFileAsync(GitCommitFileItem? item)
    {
        item ??= SelectedCommitFile;
        if (item is null
            || string.IsNullOrWhiteSpace(SelectedRepoPath))
            return;

        string? hash = !string.IsNullOrWhiteSpace(item.CommitHash)
            ? item.CommitHash
            : _selectedCommitHash;
        if (string.IsNullOrWhiteSpace(hash))
            return;

        if (!IsDiffableTextPath(item.Path))
            return;

        int version = Interlocked.Increment(ref _previewVersion);
        string repo = SelectedRepoPath;

        try
        {
            var file = new GitCommitFile(item.Path, item.OriginalPath, item.StatusCode);
            GitFileContents contents = await _gitService
                .GetCommitFileContentsAsync(repo, hash, file, _lifetime.Token)
                .ConfigureAwait(false);

            if (version != Volatile.Read(ref _previewVersion))
                return;

            if (IsOverMaxDiffSize(contents))
                return;

            await OnUiAsync(() =>
                _gitDiffPresentation.ShowGitDiff(contents.Title, contents.OldText, contents.NewText))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _previewVersion))
                await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task PreviewTimelineCommitAsync(GitCommitItem? commit)
    {
        commit ??= SelectedTimelineCommit;
        if (commit is null
            || string.IsNullOrWhiteSpace(SelectedRepoPath)
            || string.IsNullOrWhiteSpace(ActiveFilePath))
            return;

        string relative;
        try
        {
            relative = Path.GetRelativePath(SelectedRepoPath, ActiveFilePath).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return;
        }

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return;

        if (!IsDiffableTextPath(relative))
            return;

        int version = Interlocked.Increment(ref _previewVersion);
        string repo = SelectedRepoPath;
        string hash = commit.Hash;

        try
        {
            var file = new GitCommitFile(relative, OriginalPath: null, StatusCode: "M");
            GitFileContents contents = await _gitService
                .GetCommitFileContentsAsync(repo, hash, file, _lifetime.Token)
                .ConfigureAwait(false);

            if (version != Volatile.Read(ref _previewVersion))
                return;

            if (IsOverMaxDiffSize(contents))
                return;

            await OnUiAsync(() =>
                _gitDiffPresentation.ShowGitDiff(contents.Title, contents.OldText, contents.NewText))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _previewVersion))
                await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task AddToGitIgnoreAsync(GitFileStatusItem? item)
    {
        item ??= SelectedUnstagedItem ?? SelectedStagedItem;
        if (item is null || !CanMutate())
            return;

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .AddToGitIgnoreAsync(SelectedRepoPath!, item.Path, _lifetime.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
            {
                await SetStatusAsync($"Added '{item.Path}' to .gitignore.").ConfigureAwait(false);
                await RefreshStatusOnlyAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void OpenFile(GitFileStatusItem? item)
    {
        item ??= SelectedUnstagedItem ?? SelectedStagedItem;
        if (item is null || string.IsNullOrWhiteSpace(SelectedRepoPath))
            return;

        string fullPath = Path.GetFullPath(Path.Combine(SelectedRepoPath, item.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(fullPath))
            _activeDocumentManager.AddNewDocumentFromFile([fullPath]);
        else
            _messageForUserTools.OpenInExplorerHelper(Path.GetDirectoryName(fullPath) ?? SelectedRepoPath);
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task PullAsync()
    {
        if (!CanMutate())
            return;

        await SetBusyAsync(true, "Pulling…").ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService.PullAsync(SelectedRepoPath!, _lifetime.Token).ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
                await SetStatusAsync("Pull completed.").ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task PushAsync()
    {
        if (!CanMutate())
            return;

        await SetBusyAsync(true, "Pushing…").ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService.PushAsync(SelectedRepoPath!, _lifetime.Token).ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
                await SetStatusAsync("Push completed.").ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task SyncAsync()
    {
        if (!CanMutate())
            return;

        await SetBusyAsync(true, "Syncing…").ConfigureAwait(false);
        try
        {
            GitCommandResult pull = await _gitService.PullAsync(SelectedRepoPath!, _lifetime.Token).ConfigureAwait(false);
            if (!pull.Succeeded)
            {
                await ReportErrorAsync(Truncate(pull.CombinedOutput)).ConfigureAwait(false);
                return;
            }

            GitCommandResult push = await _gitService.PushAsync(SelectedRepoPath!, _lifetime.Token).ConfigureAwait(false);
            if (!push.Succeeded)
            {
                await ReportErrorAsync(Truncate(push.CombinedOutput)).ConfigureAwait(false);
                return;
            }

            await SetStatusAsync("Sync completed.").ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task PromptCreateBranchAsync()
    {
        if (!CanMutate())
            return;

        string? name = await _messageForUserTools.ShowAskForFileNameDialogAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name))
            return;

        string branchName = name.Trim();
        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .CreateBranchAsync(SelectedRepoPath!, branchName, checkout: true, _lifetime.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
            {
                await SetStatusAsync($"Created and checked out '{branchName}'.").ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task MergeBranchAsync(object? parameter)
    {
        // MenuItem may probe CanExecute/Execute with DataContext (GitViewModel); only strings are branch names.
        if (!CanMutate() || parameter is not string branchName || string.IsNullOrWhiteSpace(branchName))
            return;

        string name = branchName.Trim();
        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .MergeAsync(SelectedRepoPath!, name, _lifetime.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
            {
                await SetStatusAsync($"Merged '{name}'.").ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task CheckoutBranchAsync(object? parameter)
    {
        if (!CanMutate() || parameter is not string branchName || string.IsNullOrWhiteSpace(branchName))
            return;

        string name = branchName.Trim();
        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .CheckoutAsync(SelectedRepoPath!, name, _lifetime.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
            {
                await SetStatusAsync($"Checked out '{name}'.").ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task SaveLocalIdentityAsync()
    {
        if (!CanMutate())
            return;

        if (string.IsNullOrWhiteSpace(LocalUserName) && string.IsNullOrWhiteSpace(LocalUserEmail))
        {
            await ReportErrorAsync("Enter a name and/or email to set locally.").ConfigureAwait(false);
            return;
        }

        await SetBusyAsync(true).ConfigureAwait(false);
        try
        {
            GitCommandResult result = await _gitService
                .SetLocalUserIdentityAsync(
                    SelectedRepoPath!,
                    string.IsNullOrWhiteSpace(LocalUserName) ? null : LocalUserName,
                    string.IsNullOrWhiteSpace(LocalUserEmail) ? null : LocalUserEmail,
                    _lifetime.Token)
                .ConfigureAwait(false);

            if (!result.Succeeded)
                await ReportErrorAsync(Truncate(result.CombinedOutput)).ConfigureAwait(false);
            else
            {
                await SetStatusAsync("Local user.name / user.email saved.").ConfigureAwait(false);
                await RefreshIdentityAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    private bool CanMutate() =>
        !IsBusy && IsGitAvailable && !string.IsNullOrWhiteSpace(SelectedRepoPath);

    private async Task DiscoverAndRefreshAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool available = await _gitService.IsGitAvailableAsync(_lifetime.Token).ConfigureAwait(false);
        await OnUiAsync(() => IsGitAvailable = available).ConfigureAwait(false);
        if (!available)
        {
            await OnUiAsync(() =>
            {
                AvailableRepos.Clear();
                SelectedRepoPath = null;
                ApplyStatusFiles([]);
                Commits.Clear();
                Timeline.Clear();
                CommitFiles.Clear();
                _selectedCommitHash = null;
                StatusMessage = "Git is not installed or not on PATH.";
                ErrorMessage = null;
                IdentitySummary = null;
                OnPropertyChanged(nameof(HasTimeline));
            }).ConfigureAwait(false);
            return;
        }

        var discovered = new List<string>();
        foreach (string root in GetSearchRoots())
        {
            TryAddDiscoveredRepo(discovered, _gitService.DiscoverRepo(root));
        }

        if (!string.IsNullOrWhiteSpace(_manualRepoPath))
        {
            string? manual = NormalizeRepoPath(_gitService.DiscoverRepo(_manualRepoPath));
            if (manual is not null)
            {
                discovered.RemoveAll(r => PathsEqual(r, manual));
                discovered.Insert(0, manual);
            }
        }

        await OnUiAsync(() =>
        {
            AvailableRepos.Clear();
            foreach (string repo in discovered)
                AvailableRepos.Add(repo);

            _suppressRepoChange = true;
            try
            {
                string? selected = NormalizeRepoPath(SelectedRepoPath);
                if (selected is not null
                    && discovered.Any(r => PathsEqual(r, selected)))
                {
                    SelectedRepoPath = discovered.First(r => PathsEqual(r, selected));
                }
                else
                {
                    SelectedRepoPath = discovered.FirstOrDefault();
                }
            }
            finally
            {
                _suppressRepoChange = false;
            }
        }).ConfigureAwait(false);

        string? activePath = _activeDocumentManager.ActiveSqlDocumentViewModel?.FilePath;
        await SetActiveFileAsync(activePath).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    private IEnumerable<string> GetSearchRoots()
    {
        var roots = new List<string>();
        foreach (string path in _generalApplicationData.Config.StartsFolderPaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
                roots.Add(path);
        }

        string? active = _activeDocumentManager.ActiveSqlDocumentViewModel?.FilePath;
        if (!string.IsNullOrWhiteSpace(active))
            roots.Add(active);

        return roots;
    }

    private static bool TryAddDiscoveredRepo(List<string> discovered, string? repo)
    {
        string? normalized = NormalizeRepoPath(repo);
        if (normalized is null)
            return false;
        if (discovered.Any(r => PathsEqual(r, normalized)))
            return false;
        discovered.Add(normalized);
        return true;
    }

    private static string? NormalizeRepoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch
        {
            return Path.TrimEndingDirectorySeparator(path.Trim());
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    private async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool ownsBusy = !IsBusy;
        if (ownsBusy)
            await SetBusyAsync(true, "Refreshing…", clearError: true).ConfigureAwait(false);
        else
            await OnUiAsync(() => ErrorMessage = null).ConfigureAwait(false);

        try
        {
            await RefreshCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            if (ownsBusy)
                await SetBusyAsync(false).ConfigureAwait(false);
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (!IsGitAvailable || string.IsNullOrWhiteSpace(SelectedRepoPath))
        {
            await OnUiAsync(() =>
            {
                ApplyStatusFiles([]);
                Commits.Clear();
                Timeline.Clear();
                CommitFiles.Clear();
                _selectedCommitHash = null;
                BranchName = string.Empty;
                IdentitySummary = null;
                OnPropertyChanged(nameof(HasTimeline));
            }).ConfigureAwait(false);
            return;
        }

        int version = Interlocked.Increment(ref _refreshVersion);

        try
        {
            string repo = SelectedRepoPath;
            GitRepoStatus status = await _gitService.GetStatusAsync(repo, _lifetime.Token).ConfigureAwait(false);
            IReadOnlyList<GitCommitInfo> commits = await _gitService.GetCommitsAsync(repo, 50, _lifetime.Token).ConfigureAwait(false);
            IReadOnlyList<GitBranchInfo> branches = await _gitService.GetBranchesAsync(repo, _lifetime.Token).ConfigureAwait(false);
            GitUserIdentity identity = await _gitService.GetUserIdentityAsync(repo, _lifetime.Token).ConfigureAwait(false);
            string? upstream = await _gitService.GetUpstreamBranchAsync(repo, _lifetime.Token).ConfigureAwait(false);

            if (version != _refreshVersion)
                return;

            GitCommitItem? reloadCommit = null;
            await OnUiAsync(() =>
            {
                BranchName = status.BranchName;
                IsDetached = status.IsDetached;
                ApplyStatusFiles(status.Files);
                ApplyIdentity(identity);

                string? previousHash = _selectedCommitHash;

                Commits.Clear();
                for (int i = 0; i < commits.Count; i++)
                {
                    bool isHead = i == 0;
                    Commits.Add(GitCommitItem.From(
                        commits[i],
                        isCurrent: isHead,
                        branchLabel: isHead ? status.BranchName : null,
                        upstreamLabel: isHead ? upstream : null));
                }

                Branches.Clear();
                foreach (GitBranchInfo branch in branches)
                {
                    if (!branch.IsCurrent)
                        Branches.Add(branch.Name);
                }

                int total = StagedCount + UnstagedCount;
                StatusMessage = total == 0
                    ? $"On {BranchName} — clean"
                    : $"On {BranchName} — {StagedCount} staged, {UnstagedCount} change(s)";
                CommitCommand.NotifyCanExecuteChanged();
                StageAllAndCommitCommand.NotifyCanExecuteChanged();

                reloadCommit = previousHash is null
                    ? null
                    : Commits.FirstOrDefault(c =>
                        string.Equals(c.Hash, previousHash, StringComparison.OrdinalIgnoreCase));

                if (reloadCommit is null)
                {
                    _selectedCommitHash = null;
                    SelectedCommit = null;
                    SelectedHistoryNode = null;
                    ReplaceCommitFiles([]);
                }
                else
                {
                    SelectedCommit = reloadCommit;
                    SelectedHistoryNode = reloadCommit;
                }
            }).ConfigureAwait(false);

            await RefreshTimelineAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    private async Task RefreshIdentityAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedRepoPath))
            return;

        try
        {
            GitUserIdentity identity = await _gitService
                .GetUserIdentityAsync(SelectedRepoPath, _lifetime.Token)
                .ConfigureAwait(false);
            await OnUiAsync(() => ApplyIdentity(identity)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    private void ApplyIdentity(GitUserIdentity identity)
    {
        string name = identity.Name ?? "(not set)";
        string email = identity.Email ?? "(not set)";
        string nameScope = identity.NameIsLocal ? "local" : "global";
        string emailScope = identity.EmailIsLocal ? "local" : "global";
        IdentitySummary = $"{name} <{email}>  (name: {nameScope}, email: {emailScope})";

        if (string.IsNullOrWhiteSpace(LocalUserName) && !string.IsNullOrWhiteSpace(identity.Name))
            LocalUserName = identity.Name;
        if (string.IsNullOrWhiteSpace(LocalUserEmail) && !string.IsNullOrWhiteSpace(identity.Email))
            LocalUserEmail = identity.Email;
    }

    private async Task RefreshTimelineAsync()
    {
        if (!IsGitAvailable || string.IsNullOrWhiteSpace(SelectedRepoPath) || string.IsNullOrWhiteSpace(ActiveFilePath))
        {
            await OnUiAsync(() =>
            {
                Timeline.Clear();
                OnPropertyChanged(nameof(HasTimeline));
            }).ConfigureAwait(false);
            return;
        }

        string? repoForFile = _gitService.DiscoverRepo(ActiveFilePath);
        if (repoForFile is null
            || !PathsEqual(repoForFile, SelectedRepoPath!))
        {
            await OnUiAsync(() =>
            {
                Timeline.Clear();
                OnPropertyChanged(nameof(HasTimeline));
            }).ConfigureAwait(false);
            return;
        }

        try
        {
            IReadOnlyList<GitCommitInfo> history = await _gitService
                .GetFileHistoryAsync(SelectedRepoPath, ActiveFilePath, 30, _lifetime.Token)
                .ConfigureAwait(false);

            await OnUiAsync(() =>
            {
                Timeline.Clear();
                foreach (GitCommitInfo commit in history)
                    Timeline.Add(GitCommitItem.From(commit));
                OnPropertyChanged(nameof(HasTimeline));
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            await OnUiAsync(() =>
            {
                Timeline.Clear();
                OnPropertyChanged(nameof(HasTimeline));
            }).ConfigureAwait(false);
        }
    }

    public async Task LoadCommitFilesAsync(GitCommitItem? commit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int version = Interlocked.Increment(ref _commitFilesVersion);

        if (commit is null || string.IsNullOrWhiteSpace(SelectedRepoPath))
        {
            if (version != Volatile.Read(ref _commitFilesVersion))
                return;

            await OnUiAsync(() =>
            {
                if (version != Volatile.Read(ref _commitFilesVersion))
                    return;
                _selectedCommitHash = null;
                ReplaceCommitFiles([]);
            }).ConfigureAwait(false);
            return;
        }

        try
        {
            string hash = commit.Hash;
            string repo = SelectedRepoPath;
            IReadOnlyList<GitCommitFile> files = await _gitService
                .GetCommitFilesAsync(repo, hash, _lifetime.Token)
                .ConfigureAwait(false);

            if (version != Volatile.Read(ref _commitFilesVersion))
                return;

            var items = files.Select(f => GitCommitFileItem.From(f, hash)).ToList();
            await OnUiAsync(() =>
            {
                if (version != Volatile.Read(ref _commitFilesVersion))
                    return;
                _selectedCommitHash = hash;
                ReplaceCommitFiles(items);
                commit.ReplaceFiles(items);
                commit.IsExpanded = true;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _commitFilesVersion))
                await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    private void ReplaceCommitFiles(IReadOnlyList<GitCommitFileItem> items)
    {
        CommitFiles.Clear();
        foreach (GitCommitFileItem item in items)
            CommitFiles.Add(item);
    }

    private async Task RefreshStatusOnlyAsync()
    {
        if (!IsGitAvailable || string.IsNullOrWhiteSpace(SelectedRepoPath))
            return;

        try
        {
            GitRepoStatus status = await _gitService.GetStatusAsync(SelectedRepoPath, _lifetime.Token).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                BranchName = status.BranchName;
                IsDetached = status.IsDetached;
                ApplyStatusFiles(status.Files);
                int total = StagedCount + UnstagedCount;
                StatusMessage = total == 0
                    ? $"On {BranchName} — clean"
                    : $"On {BranchName} — {StagedCount} staged, {UnstagedCount} change(s)";
                CommitCommand.NotifyCanExecuteChanged();
                StageAllAndCommitCommand.NotifyCanExecuteChanged();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    private void ApplyStatusFiles(IReadOnlyList<GitFileStatus> files)
    {
        var staged = new List<GitFileStatusItem>();
        var unstaged = new List<GitFileStatusItem>();
        foreach (GitFileStatus file in files)
        {
            var item = GitFileStatusItem.From(file);
            if (item.IsStaged)
                staged.Add(item);
            if (item.IsUnstaged || item.Kind == GitChangeKind.Untracked)
                unstaged.Add(item);
        }

        StagedChanges = staged;
        UnstagedChanges = unstaged;
    }

    private static readonly HashSet<string> DiffableTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Programming languages
        ".cs", ".csproj", ".sln", ".vb", ".fs", ".fsx", ".java", ".kt", ".kts", ".scala",
        ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".hxx",
        ".py", ".pyw", ".rb", ".php", ".pl", ".pm", ".js", ".jsx", ".ts", ".tsx",
        ".mjs", ".cjs", ".vue", ".svelte", ".swift", ".m", ".mm", ".dart",
        ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".bat", ".cmd", ".lua", ".r",
        // SQL
        ".sql", ".ddl", ".dml",
        // Web
        ".html", ".htm", ".css", ".scss", ".sass", ".less", ".xml", ".svg",
        // Data / config
        ".json", ".jsonc", ".yaml", ".yml", ".toml",
        ".ini", ".cfg", ".conf", ".config", ".properties", ".env",
        ".editorconfig", ".gitignore", ".dockerignore",
        // Docs / plain text
        ".md", ".markdown", ".txt", ".log", ".csv", ".tsv"
    };

    private static readonly HashSet<string> KnownTextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dockerfile", "makefile", "rakefile", "gemfile", "license", "licence",
        "readme", "changelog", "justfile", "procfile"
    };

    private static bool IsDiffableTextPath(string path)
    {
        string name = Path.GetFileName(path);
        string ext = Path.GetExtension(name);
        if (DiffableTextExtensions.Contains(ext))
            return true;
        return ext.Length == 0 && KnownTextFileNames.Contains(name);
    }

    /// <summary>Hard size guard: never show a diff for files over this size, regardless of extension.</summary>
    private const int MaxDiffContentLength = 5_000_000;

    private static bool IsOverMaxDiffSize(GitFileContents contents) =>
        contents.OldText.Length + contents.NewText.Length > MaxDiffContentLength;

    private static bool IsOverMaxDiffSize(string repoPath, string relativePath)
    {
        try
        {
            string fullPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                return false;
            return new FileInfo(fullPath).Length > MaxDiffContentLength;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReportErrorAsync(string message)
    {
        await OnUiAsync(() =>
        {
            ErrorMessage = message;
            StatusMessage = null;
        }).ConfigureAwait(false);
        _messageForUserTools.ShowSimpleMessageBoxInstance(message, "Git");
    }

    private static Task OnUiAsync(Action action) => UiThreadMarshal.InvokeAsync(action);

    /// <summary>
    /// Avalonia buttons subscribe to CanExecuteChanged; NotifyCanExecuteChanged must run on the UI thread.
    /// </summary>
    private Task SetBusyAsync(bool busy, string? statusMessage = null, bool clearError = false) =>
        OnUiAsync(() =>
        {
            IsBusy = busy;
            if (clearError)
                ErrorMessage = null;
            if (statusMessage is not null)
                StatusMessage = statusMessage;
        });

    private Task SetStatusAsync(string? status) =>
        OnUiAsync(() => StatusMessage = status);

    private static string Truncate(string text, int max = 800)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Git command failed.";
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}

public sealed class GitFileStatusItem
{
    public required string Path { get; init; }
    public string? OriginalPath { get; init; }
    public GitChangeKind Kind { get; init; }
    public bool IsStaged { get; init; }
    public bool IsUnstaged { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string IndexStatus { get; init; } = string.Empty;
    public string WorkTreeStatus { get; init; } = string.Empty;

    public string StatusLetter
    {
        get
        {
            if (string.IsNullOrEmpty(StatusCode))
                return "?";
            char c = StatusCode[0];
            return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : StatusCode[..1];
        }
    }

    public IBrush StatusForeground => StatusLetter switch
    {
        "A" => Brushes.LimeGreen,
        "D" => Brushes.IndianRed,
        "R" => Brushes.DeepSkyBlue,
        "C" => Brushes.DeepSkyBlue,
        "M" => Brushes.Orange,
        _ => Brushes.Gray
    };

    public string DisplayText => string.IsNullOrEmpty(OriginalPath)
        ? $"{StatusCode}  {Path}"
        : $"{StatusCode}  {OriginalPath} → {Path}";

    public static GitFileStatusItem From(GitFileStatus status) => new()
    {
        Path = status.Path,
        OriginalPath = status.OriginalPath,
        Kind = status.Kind,
        IsStaged = status.IsStaged,
        IsUnstaged = status.IsUnstaged,
        StatusCode = status.DisplayStatus,
        IndexStatus = status.IndexStatus,
        WorkTreeStatus = status.WorkTreeStatus
    };

    public GitFileStatus ToStatus() => new(
        Path,
        OriginalPath,
        Kind,
        IsStaged,
        IsUnstaged,
        IndexStatus,
        WorkTreeStatus);

    public GitFileStatusItem AsStagedOnlyPreview() => new()
    {
        Path = Path,
        OriginalPath = OriginalPath,
        Kind = Kind,
        IsStaged = true,
        IsUnstaged = false,
        StatusCode = StatusCode,
        IndexStatus = IndexStatus,
        WorkTreeStatus = string.Empty
    };

    public override string ToString() => DisplayText;
}

public sealed partial class GitCommitItem : ObservableObject
{
    public required string Hash { get; init; }
    public required string ShortHash { get; init; }
    public required string Author { get; init; }
    public required DateTimeOffset AuthorDate { get; init; }
    public required string Subject { get; init; }
    public bool IsCurrent { get; init; }
    public string? BranchLabel { get; init; }
    public string? UpstreamLabel { get; init; }

    public ObservableCollection<GitCommitFileItem> Files { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBody))]
    public partial string Body { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStats))]
    [NotifyPropertyChangedFor(nameof(FilesChangedText))]
    [NotifyPropertyChangedFor(nameof(InsertionsText))]
    [NotifyPropertyChangedFor(nameof(DeletionsText))]
    [NotifyPropertyChangedFor(nameof(HasInsertions))]
    [NotifyPropertyChangedFor(nameof(HasDeletions))]
    public partial int FilesChanged { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsertionsText))]
    [NotifyPropertyChangedFor(nameof(HasInsertions))]
    public partial int Insertions { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeletionsText))]
    [NotifyPropertyChangedFor(nameof(HasDeletions))]
    public partial int Deletions { get; private set; }

    [ObservableProperty]
    public partial bool TooltipLoaded { get; private set; }

    public string RelativeDate => GitOutputParser.FormatRelativeDate(AuthorDate);

    public string RelativeDateLong => GitOutputParser.FormatRelativeDateLong(AuthorDate);

    public string AbsoluteDateText =>
        AuthorDate == DateTimeOffset.MinValue
            ? string.Empty
            : AuthorDate.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture);

    public string HeaderDateText =>
        string.IsNullOrEmpty(AbsoluteDateText)
            ? RelativeDateLong
            : $"{RelativeDateLong} ({AbsoluteDateText})";

    public string MetaText => $"{Author}  ·  {RelativeDate}  ·  {ShortHash}";

    public string DisplayText => $"{Subject}  {Author}  {RelativeDate}";

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public bool HasStats => FilesChanged > 0 || Insertions > 0 || Deletions > 0;
    public bool HasInsertions => Insertions > 0;
    public bool HasDeletions => Deletions > 0;
    public bool HasUpstream => !string.IsNullOrWhiteSpace(UpstreamLabel);

    public string FilesChangedText
    {
        get
        {
            if (!HasStats)
                return string.Empty;
            string files = FilesChanged == 1 ? "1 file changed" : $"{FilesChanged} files changed";
            if (HasInsertions || HasDeletions)
                return files + ", ";
            return files;
        }
    }

    public string InsertionsText =>
        Insertions <= 0 ? string.Empty : $"{Insertions} insertions(+)";

    public string DeletionsText
    {
        get
        {
            if (Deletions <= 0)
                return string.Empty;
            return HasInsertions ? $", {Deletions} deletions(-)" : $"{Deletions} deletions(-)";
        }
    }

    public static GitCommitItem From(
        GitCommitInfo commit,
        bool isCurrent = false,
        string? branchLabel = null,
        string? upstreamLabel = null) => new()
    {
        Hash = commit.Hash,
        ShortHash = commit.ShortHash,
        Author = commit.Author,
        AuthorDate = commit.AuthorDate,
        Subject = commit.Subject,
        IsCurrent = isCurrent,
        BranchLabel = isCurrent ? branchLabel : null,
        UpstreamLabel = isCurrent ? upstreamLabel : null
    };

    public void ReplaceFiles(IEnumerable<GitCommitFileItem> items)
    {
        Files.Clear();
        foreach (GitCommitFileItem item in items)
            Files.Add(item);
    }

    public void ApplyTooltip(GitCommitTooltipInfo info)
    {
        Body = info.Body?.Trim() ?? string.Empty;
        FilesChanged = info.FilesChanged;
        Insertions = info.Insertions;
        Deletions = info.Deletions;
        TooltipLoaded = true;
    }

    public override string ToString() => DisplayText;
}

public sealed class GitCommitFileItem
{
    public required string Path { get; init; }
    public string? OriginalPath { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string? CommitHash { get; init; }

    /// <summary>Present so TreeViewItem IsExpanded binding does not fail on file nodes.</summary>
    public bool IsExpanded
    {
        get => false;
        set { }
    }

    public string FileName => System.IO.Path.GetFileName(Path.Replace('\\', '/'));

    public string DirectoryHint
    {
        get
        {
            string normalized = Path.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            return slash <= 0 ? string.Empty : normalized[..slash].Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
    }

    public string StatusLetter
    {
        get
        {
            if (string.IsNullOrEmpty(StatusCode))
                return "M";
            char c = StatusCode[0];
            return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : StatusCode[..1];
        }
    }

    public IBrush StatusForeground => StatusLetter switch
    {
        "A" => Brushes.LimeGreen,
        "D" => Brushes.IndianRed,
        "R" => Brushes.DeepSkyBlue,
        "C" => Brushes.DeepSkyBlue,
        "M" => Brushes.Orange,
        _ => Brushes.Gray
    };

    public string DisplayText => string.IsNullOrEmpty(OriginalPath)
        ? $"{StatusCode}  {Path}"
        : $"{StatusCode}  {OriginalPath} → {Path}";

    public static GitCommitFileItem From(GitCommitFile file, string? commitHash = null) => new()
    {
        Path = file.Path,
        OriginalPath = file.OriginalPath,
        StatusCode = file.StatusCode,
        CommitHash = commitHash
    };

    public override string ToString() => DisplayText;
}
