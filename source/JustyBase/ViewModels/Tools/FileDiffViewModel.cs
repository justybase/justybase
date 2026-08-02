using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffPlex;
using DiffPlex.DiffBuilder;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels;

public partial class DiffLineViewModel : ObservableObject
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "Unchanged";
    
    public IBrush Background => Type switch
    {
        "Deleted" => new SolidColorBrush(Color.Parse("#40FF0000")),
        "Inserted" => new SolidColorBrush(Color.Parse("#4000FF00")),
        "Modified" => new SolidColorBrush(Color.Parse("#40FFFF00")),
        "Imaginary" => new SolidColorBrush(Color.Parse("#20000000")),
        _ => Brushes.Transparent
    };
}

public partial class FileDiffViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string OldText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<DiffLineViewModel> OldLines { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DiffLineViewModel> NewLines { get; set; } = [];

    public Action? ReloadAction;
    public Action? KeepCurrentAction;
    public Action? CloseAction;

    public FileDiffViewModel()
    {
        BuildDiff();
    }

    public void SetTexts(string oldText, string newText)
    {
        OldText = oldText;
        NewText = newText;
    }

    partial void OnOldTextChanged(string value)
    {
        BuildDiff();
    }

    partial void OnNewTextChanged(string value)
    {
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
    private void Reload()
    {
        ReloadAction?.Invoke();
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void KeepCurrent()
    {
        KeepCurrentAction?.Invoke();
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void Dismiss()
    {
        CloseAction?.Invoke();
    }
}
