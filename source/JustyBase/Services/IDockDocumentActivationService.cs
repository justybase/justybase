using Dock.Model.Core;

namespace JustyBase.Services;

public interface IDockDocumentActivationService
{
    T? GetDocumentOfType<T>(IEnumerable<IDockable> visibleDockables) where T : class, IDockable;

    T EnsureDocument<T>(IList<IDockable> visibleDockables, Func<T> createDocument, bool recreateExisting = false)
        where T : class, IDockable;
}

public sealed class DockDocumentActivationService : IDockDocumentActivationService
{
    public T? GetDocumentOfType<T>(IEnumerable<IDockable> visibleDockables) where T : class, IDockable
    {
        ArgumentNullException.ThrowIfNull(visibleDockables);

        T? result = null;
        foreach (IDockable dockable in visibleDockables)
        {
            if (dockable is T typedDockable)
            {
                result = typedDockable;
            }
        }

        return result;
    }

    public T EnsureDocument<T>(IList<IDockable> visibleDockables, Func<T> createDocument, bool recreateExisting = false)
        where T : class, IDockable
    {
        ArgumentNullException.ThrowIfNull(visibleDockables);
        ArgumentNullException.ThrowIfNull(createDocument);

        T? existingDocument = GetDocumentOfType<T>(visibleDockables);
        if (recreateExisting && existingDocument is not null)
        {
            visibleDockables.Remove(existingDocument);
            existingDocument = null;
        }

        existingDocument ??= createDocument();
        if (!visibleDockables.Contains(existingDocument))
        {
            visibleDockables.Add(existingDocument);
        }

        return existingDocument;
    }
}
