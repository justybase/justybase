namespace JustyBase.Services.DataCopy;

/// <summary>Host-level dialog entry so ViewModels never reference window types.</summary>
public interface IDataCopyDialogService
{
    Task ShowAsync(
        string sourceConnectionName,
        string sourceDatabase,
        string sourceSchema,
        string sourceTable,
        bool isView);
}
