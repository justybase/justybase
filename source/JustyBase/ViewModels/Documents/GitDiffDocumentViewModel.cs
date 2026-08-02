using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using System.Text;

namespace JustyBase.ViewModels.Documents;

public sealed partial class GitDiffDocumentViewModel : DocumentBaseVM
{
    public const string DocumentId = "GitDiff";
    private const string DefaultEditorFontFamily = "Cascadia Code,JetBrains Mono,Consolas,Monaco,Menlo,Monospace";

    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDocumentFontService _documentFontService;
    private List<int> _changeLineNumbers = [];

    [ObservableProperty]
    public partial string OldDisplayText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NewDisplayText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ChangeType> OldLineKinds { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ChangeType> NewLineKinds { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(GoToPrevCanExecute))]
    [NotifyPropertyChangedFor(nameof(GoToNextCanExecute))]
    [NotifyCanExecuteChangedFor(nameof(GoToPreviousChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToNextChangeCommand))]
    public partial int CurrentChangeIndex { get; private set; } = -1;

    public int ChangeCount => _changeLineNumbers.Count;
    public bool HasChanges => _changeLineNumbers.Count > 0;
    public bool GoToPrevCanExecute => CurrentChangeIndex > 0;
    public bool GoToNextCanExecute => CurrentChangeIndex < _changeLineNumbers.Count - 1;

    public int ContentVersion { get; private set; }

    /// <summary>Same default size as SQL documents (<see cref="ISomeEditorOptions.DEFAULT_DOCUMENT_FONT_SIZE"/> / config).</summary>
    public double EditorFontSize
    {
        get
        {
            int size = _generalApplicationData.Config.DefaultFontSizeForDocuments;
            return size > 0 ? size : ISomeEditorOptions.DEFAULT_DOCUMENT_FONT_SIZE;
        }
    }

    /// <summary>Same family as SQL editor (configured DocumentFontName with Cascadia fallback stack).</summary>
    public FontFamily EditorFontFamily =>
        _documentFontService.GetFontByName(_generalApplicationData.Config.DocumentFontName)
        ?? new FontFamily(DefaultEditorFontFamily);

    public GitDiffDocumentViewModel(
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDocumentCloseDecisionService documentCloseDecisionService,
        IActiveDocumentManager activeDocumentManager,
        IDocumentFontService documentFontService)
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        _generalApplicationData = generalApplicationData;
        _documentFontService = documentFontService;
        Id = DocumentId;
        Title = "Diff";
        CanClose = true;
        CanFloat = false;
        DockCapabilityHelper.SyncOverridesFromFlags(this);
    }

    public void SetContents(string title, string oldText, string newText)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Diff" : $"diff: {title}";
        BuildSideBySide(oldText ?? string.Empty, newText ?? string.Empty);
        ContentVersion++;
        OnPropertyChanged(nameof(ContentVersion));
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontFamily));
    }

    /// <summary>Returns the 1-based line number to scroll to, or -1 if none.</summary>
    public int GetCurrentScrollTarget()
    {
        if (CurrentChangeIndex < 0 || CurrentChangeIndex >= _changeLineNumbers.Count)
            return -1;
        return _changeLineNumbers[CurrentChangeIndex];
    }

    [RelayCommand(CanExecute = nameof(GoToPrevCanExecute))]
    private void GoToPreviousChange()
    {
        if (CurrentChangeIndex > 0)
            CurrentChangeIndex--;
    }

    [RelayCommand(CanExecute = nameof(GoToNextCanExecute))]
    private void GoToNextChange()
    {
        if (CurrentChangeIndex < _changeLineNumbers.Count - 1)
            CurrentChangeIndex++;
    }

    private void ComputeChangeLineIndices()
    {
        var indices = new List<int>();
        int count = Math.Min(OldLineKinds.Count, NewLineKinds.Count);
        for (int i = 0; i < count; i++)
        {
            ChangeType oldKind = OldLineKinds[i];
            ChangeType newKind = NewLineKinds[i];
            if (oldKind is ChangeType.Deleted or ChangeType.Modified or ChangeType.Inserted
                || newKind is ChangeType.Deleted or ChangeType.Modified or ChangeType.Inserted)
            {
                indices.Add(i + 1); // 1-based line number
            }
        }
        _changeLineNumbers = indices;
        CurrentChangeIndex = indices.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(HasChanges));
        GoToPreviousChangeCommand.NotifyCanExecuteChanged();
        GoToNextChangeCommand.NotifyCanExecuteChanged();
    }

    private void BuildSideBySide(string oldText, string newText)
    {
        var diff = new SideBySideDiffBuilder(new Differ()).BuildDiffModel(oldText, newText);

        var oldSb = new StringBuilder();
        var newSb = new StringBuilder();
        var oldKinds = new List<ChangeType>();
        var newKinds = new List<ChangeType>();

        int count = Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count);
        for (int i = 0; i < count; i++)
        {
            DiffPiece? oldLine = i < diff.OldText.Lines.Count ? diff.OldText.Lines[i] : null;
            DiffPiece? newLine = i < diff.NewText.Lines.Count ? diff.NewText.Lines[i] : null;

            if (i > 0)
            {
                oldSb.AppendLine();
                newSb.AppendLine();
            }

            oldSb.Append(oldLine?.Text ?? string.Empty);
            newSb.Append(newLine?.Text ?? string.Empty);
            oldKinds.Add(oldLine?.Type ?? ChangeType.Imaginary);
            newKinds.Add(newLine?.Type ?? ChangeType.Imaginary);
        }

        OldDisplayText = oldSb.ToString();
        NewDisplayText = newSb.ToString();
        OldLineKinds = oldKinds;
        NewLineKinds = newKinds;
        ComputeChangeLineIndices();
    }
}
