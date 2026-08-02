using System.Collections.Specialized;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.ViewModels.Views;

namespace JustyBase.Services.Docking;

public interface IDockSidePanelService
{
    ProportionalDock? ResolveMiddleDock(IRootDock? rootDock, ProportionalDock? cachedMiddleDock);
    void HideSideElements(ProportionalDock layoutDock, ProportionalDock middleDock, List<IDockable> hiddenDockables);
    void RestoreSideElements(ProportionalDock layoutDock, ProportionalDock middleDock, List<IDockable> hiddenDockables);
}

public sealed class DockSidePanelService : IDockSidePanelService
{
    public ProportionalDock? ResolveMiddleDock(IRootDock? rootDock, ProportionalDock? cachedMiddleDock)
    {
        if (cachedMiddleDock is not null)
        {
            return cachedMiddleDock;
        }

        if (rootDock?.ActiveDockable is MainViewModel { ActiveDockable: ProportionalDock layoutDock })
        {
            return layoutDock.VisibleDockables?.OfType<ProportionalDock>().FirstOrDefault(dock => dock.Title == "MiddleDock");
        }

        return null;
    }

    public void HideSideElements(ProportionalDock layoutDock, ProportionalDock middleDock, List<IDockable> hiddenDockables)
    {
        hiddenDockables.Clear();
        var items = layoutDock.VisibleDockables;
        if (items is null)
        {
            return;
        }

        var sideDock = items
            .OfType<ProportionalDock>()
            .FirstOrDefault(dock => dock != middleDock && dock.Title != "MiddleDock" && !IsAiChatDock(dock));

        if (sideDock is null)
        {
            return;
        }

        int sideIndex = items.IndexOf(sideDock);
        int middleIndex = items.IndexOf(middleDock);
        if (middleIndex < 0)
        {
            middleIndex = items.ToList().FindIndex(d => d.Title == "MiddleDock");
        }

        if (sideIndex < 0)
        {
            return;
        }

        hiddenDockables.Add(sideDock);

        // Remove only the splitter between the side column and middle — keep the AI Chat splitter.
        if (middleIndex >= 0 && sideIndex < middleIndex
            && sideIndex + 1 < items.Count
            && items[sideIndex + 1] is IProportionalDockSplitter splitterAfter)
        {
            hiddenDockables.Add(splitterAfter);
        }
        else if (middleIndex >= 0 && sideIndex > middleIndex
            && sideIndex - 1 >= 0
            && items[sideIndex - 1] is IProportionalDockSplitter splitterBefore)
        {
            hiddenDockables.Add(splitterBefore);
        }

        foreach (var item in hiddenDockables)
        {
            items.Remove(item);
        }

        middleDock.Proportion = items.Any(IsAiChatDock) ? 0.75 : 1.0;
        NotifyVisibleDockablesChanged(layoutDock, items);
    }

    public void RestoreSideElements(ProportionalDock layoutDock, ProportionalDock middleDock, List<IDockable> hiddenDockables)
    {
        var items = layoutDock.VisibleDockables;
        var sideDock = hiddenDockables.OfType<ProportionalDock>().FirstOrDefault();
        if (items is null || sideDock is null)
        {
            hiddenDockables.Clear();
            return;
        }

        // Mutate in place — replacing VisibleDockables with a new list reuses the same
        // dockable instances and Dock.Avalonia can duplicate them in the visual tree.
        sideDock.Proportion = 0.25;
        items.Insert(0, new ProportionalDockSplitter());
        items.Insert(0, sideDock);
        middleDock.Proportion = items.Any(IsAiChatDock) ? 0.5 : 0.75;
        hiddenDockables.Clear();
        NotifyVisibleDockablesChanged(layoutDock, items);
    }

    /// <summary>
    /// Plain List mutations do not refresh Dock visuals. CreateList (ObservableCollection) notifies
    /// via CollectionChanged; for non-notifying lists, re-assign the same instance to raise PropertyChanged.
    /// </summary>
    private static void NotifyVisibleDockablesChanged(ProportionalDock layoutDock, IList<IDockable> items)
    {
        if (items is INotifyCollectionChanged)
        {
            return;
        }

        layoutDock.VisibleDockables = null;
        layoutDock.VisibleDockables = items;
    }

    internal static bool IsAiChatDock(IDockable dockable)
    {
        if (dockable.Id == "AiChatDock")
        {
            return true;
        }

        return dockable is IDock dock
            && dock.VisibleDockables?.Count == 1
            && dock.VisibleDockables[0].Id == "AiChat";
    }
}
