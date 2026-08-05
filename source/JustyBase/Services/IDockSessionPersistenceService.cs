using Dock.Model.Core;
using JustyBase.Common.Models;
using System.Text.Json;

namespace JustyBase.Services;

public sealed record DockSessionSaveRequest(
    string? SelectedTabId,
    OfflineDocumentContainer OfflineDocumentContainer,
    IEnumerable<IDockable> VisibleDockables,
    Action<string> SaveEncodedText);

public interface IDockSessionPersistenceService
{
    void SaveSession(DockSessionSaveRequest request);
}

public sealed class DockSessionPersistenceService : IDockSessionPersistenceService
{
    public void SaveSession(DockSessionSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.OfflineDocumentContainer);
        ArgumentNullException.ThrowIfNull(request.VisibleDockables);
        ArgumentNullException.ThrowIfNull(request.SaveEncodedText);

        // No active document tab is valid (e.g. New Layout while only tool panes are open).
        request.OfflineDocumentContainer.SelectedTabId = string.IsNullOrWhiteSpace(request.SelectedTabId)
            ? null
            : request.SelectedTabId;
        SortOfflineTabs(request.OfflineDocumentContainer, request.VisibleDockables);

        string content = JsonSerializer.Serialize(
            request.OfflineDocumentContainer,
            MyJsonContextOfflineDocumentContainer.Default.OfflineDocumentContainer);

        request.SaveEncodedText(content);
    }

    private static void SortOfflineTabs(OfflineDocumentContainer offlineDocumentContainer, IEnumerable<IDockable> visibleDockables)
    {
        List<IDockable> orderedDockables = visibleDockables.ToList();
        Dictionary<string, int> visibleDockOrder = [];
        for (int index = 0; index < orderedDockables.Count; index++)
        {
            visibleDockOrder[orderedDockables[index].Id] = index;
        }

        foreach (string documentId in offlineDocumentContainer.SqlOfflineDocumentDictionary.Keys)
        {
            visibleDockOrder.TryAdd(documentId, int.MaxValue);
        }

        offlineDocumentContainer.SqlOfflineDocumentDictionary = offlineDocumentContainer.SqlOfflineDocumentDictionary
            .OrderBy(pair => visibleDockOrder[pair.Key])
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}
