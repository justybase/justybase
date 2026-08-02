using JustyBase.ViewModels;

namespace JustyBase.Services;

public interface INetezzaMaintenanceDialogService
{
    Task<string?> ShowAsync(NetezzaMaintenanceDialogKind kind, string qualifiedTable);
}
