namespace JustyBase.Services.DataGrid;

public enum ResultGridToolbarAction
{
    None = 0,
    CopyAsCsvClipboard = 1,
    CopyAsCsvClipboardHeaders = 2,
    CopyRowValues = 3,
    CopyAsExcelFileClipboard = 4,
    OpenAsExcelFileClipboard = 5,
    SaveAsExcelFile = 6,
    CopyAsHtml = 7,
    CopySelectedCellsCurrentColumn = 8,
    CopySelectedCellsCurrentColumnRange = 9
}

public interface IResultGridActionRoutingService
{
    ResultGridToolbarAction Resolve(string? action);
    bool RequiresTableReader(ResultGridToolbarAction action);
}
