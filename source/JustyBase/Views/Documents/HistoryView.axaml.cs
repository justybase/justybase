using JustyBase.Common.Models;
using JustyBase.ViewModels.Documents;
using System.Text;

namespace JustyBase.Views.Documents;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        hisotryGrid.KeyDown += HisotryGrid_KeyDown;
        textEditor.SyntaxHighlighting = AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinition("SQL");
    }
    private HistoryViewModel? ViewModel => this.DataContext as HistoryViewModel;

    private void FavoriteCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not HistoryEntry entry || ViewModel is null)
        {
            return;
        }

        ViewModel.ToggleFavoriteEntry(entry);
        checkBox.IsChecked = entry.IsFavorite;
        e.Handled = true;
    }

    private async void HisotryGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            var SelectedItems = hisotryGrid.SelectedItems;
            StringBuilder sb = new();
            sb.Append("★\tDate\tStatus\tDuration\tConnection\tDatabase\tSQL");
            if (SelectedItems.Count > 0)
            {
                sb.AppendLine();
                for (int index = 0; index < SelectedItems.Count; index++)
                {
                    if (SelectedItems[index] is HistoryEntry historyEntry)
                    {
                        sb.Append(historyEntry.IsFavorite ? "★" : "");
                        sb.Append('\t');
                        sb.Append(historyEntry.Date.ToString());
                        sb.Append('\t');
                        sb.Append(historyEntry.StatusText);
                        sb.Append('\t');
                        sb.Append(historyEntry.DurationText);
                        sb.Append('\t');
                        sb.Append(historyEntry.Connection);
                        sb.Append('\t');
                        sb.Append(historyEntry.Database);
                        sb.Append('\t');
                        sb.AppendLine(historyEntry.SQL);
                    }
                }
            }
            await (ViewModel?.Clipboard)?.SetTextAsync(sb.ToString());
            e.Handled = true;
        }
    }
}
