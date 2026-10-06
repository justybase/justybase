using JustyBase.Services;

namespace JustyBase.Views.OtherDialogs;

public sealed record ReplacementPreviewLine(int Line, string Before, string After);
public sealed record ReplacementPreviewRow(string Path, string CountLabel,
    IReadOnlyList<ReplacementPreviewLine> Changes);

public partial class ReplacePreviewWindow : Window
{
    public ReplacePreviewWindow(IReadOnlyList<ContentReplacement> replacements, IReadOnlyList<string> skipped)
    {
        Changes = replacements.Select(r => new ReplacementPreviewRow(r.File.RelativePath,
            $"{r.Count} replacements", r.Changes.Select(change => new ReplacementPreviewLine(
                change.Line, change.Before, change.After)).ToArray())).ToArray();
        Summary = $"{replacements.Sum(x => x.Count)} replacements in {replacements.Count} files" +
            (skipped.Count > 0 ? $". Skipped: {string.Join("; ", skipped)}" : "");
        InitializeComponent();
        DataContext = this;
    }

    public IReadOnlyList<ReplacementPreviewRow> Changes { get; }
    public string Summary { get; }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
    private void Apply_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
}
