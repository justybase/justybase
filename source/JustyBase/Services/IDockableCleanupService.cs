using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services;

public interface IDockableCleanupService
{
    void CleanupDockable(IDockable dockable, Action<string>? clearSqlDocumentResults);
}

public sealed class DockableCleanupService : IDockableCleanupService
{
    public void CleanupDockable(IDockable dockable, Action<string>? clearSqlDocumentResults)
    {
        ArgumentNullException.ThrowIfNull(dockable);

        ViewLocator.RemoveFromCache(dockable);

        if (dockable is ICleanableViewModel cleanableViewModel)
        {
            cleanableViewModel.DoCleanup();
        }

        if (dockable is SqlDocumentViewModel sqlDocumentViewModel)
        {
            clearSqlDocumentResults?.Invoke(sqlDocumentViewModel.Id);
        }
    }
}
