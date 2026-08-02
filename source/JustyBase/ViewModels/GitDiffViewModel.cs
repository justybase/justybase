using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffPlex;
using DiffPlex.DiffBuilder;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels;

public partial class GitDiffViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Diff";

    [ObservableProperty]
    public partial string OldText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<DiffLineViewModel> OldLines { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DiffLineViewModel> NewLines { get; set; } = [];

    public Action? CloseAction { get; set; }

    public void SetContents(string title, string oldText, string newText)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Diff" : title;
        OldText = oldText ?? string.Empty;
        NewText = newText ?? string.Empty;
        BuildDiff();
    }

    private void BuildDiff()
    {
        OldLines.Clear();
        NewLines.Clear();

        var diffBuilder = new SideBySideDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(OldText, NewText);

        foreach (var line in diff.OldText.Lines)
        {
            OldLines.Add(new DiffLineViewModel
            {
                Text = line.Text ?? string.Empty,
                Type = line.Type.ToString()
            });
        }

        foreach (var line in diff.NewText.Lines)
        {
            NewLines.Add(new DiffLineViewModel
            {
                Text = line.Text ?? string.Empty,
                Type = line.Type.ToString()
            });
        }
    }

    [RelayCommand]
    private void Close() => CloseAction?.Invoke();
}
