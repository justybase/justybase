using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Documents;

public sealed partial class HistoryViewModel
{
    private readonly HistoryService _historyService;

    [ObservableProperty]
    public partial TextDocument Doc { get; set; } = new TextDocument();

    public HistoryEntry? SelectedItem
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Doc.Text = BuildDetailText(field);
                OnPropertyChanged(nameof(Doc));
                (RerunWithParamsCmd as CommunityToolkit.Mvvm.Input.RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<HistoryEntry> FilteredHistoryItems { get; } = [];
    private List<HistoryEntry>? HistoryItemsCollection => _historyService.HistoryItemsCollection;

    public void RefreshFilteredItems()
    {
        FilteredHistoryItems.Clear();
        foreach (var item in _historyService.Filter(
                     SearchTxt,
                     FavoritesOnly,
                     SelectedStatusFilter.Status,
                     SelectedDurationFilter.Preset))
        {
            FilteredHistoryItems.Add(item);
        }
    }

    public void ToggleFavoriteEntry(HistoryEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _historyService.ToggleFavorite(entry);
        if (FavoritesOnly)
        {
            RefreshFilteredItems();
        }
    }

    public void RerunWithParams()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var doc = ActiveDocumentManager.AddNewDocument(SelectedItem.SQL);
        doc.TrySetConnection(SelectedItem.Connection);
        if (!string.IsNullOrWhiteSpace(SelectedItem.Database))
        {
            doc.SelectedDatabase = SelectedItem.Database;
        }

        // RunSqlAsync already prompts via SqlParameterWindow / SqlVariableProcessor when variables are present.
        if (doc.RunSqlCommand.CanExecute("Grid"))
        {
            doc.RunSqlCommand.Execute("Grid");
        }
    }

    private static string BuildDetailText(HistoryEntry? entry)
    {
        if (entry is null)
        {
            return "";
        }

        if (string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            return entry.SQL;
        }

        return $"{entry.SQL}\n\n-- ERROR --\n{entry.ErrorMessage}";
    }
}
