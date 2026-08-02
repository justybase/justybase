namespace JustyBase.Services.DataGrid;

public sealed class ResultGridActionRoutingService : IResultGridActionRoutingService
{
    public ResultGridToolbarAction Resolve(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return ResultGridToolbarAction.None;
        }

        if (action.StartsWith("CopyAsCsvClipboardHeaders", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopyAsCsvClipboardHeaders;
        }

        if (action.StartsWith("CopyAsCsvClipboard", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopyAsCsvClipboard;
        }

        if (action.StartsWith("CopyAsExcelFileClipboard", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopyAsExcelFileClipboard;
        }

        if (action.StartsWith("OpenAsExcelFileClipboard", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.OpenAsExcelFileClipboard;
        }

        if (action.StartsWith("SaveAsExcelFile", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.SaveAsExcelFile;
        }

        if (action.StartsWith("CopyAsHtml", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopyAsHtml;
        }

        if (action.StartsWith("CopySelectecCellsCurrentColumn2", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopySelectedCellsCurrentColumnRange;
        }

        if (action.StartsWith("CopySelectecCellsCurrentColumn", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopySelectedCellsCurrentColumn;
        }

        if (action.StartsWith("CopyRowValues", StringComparison.Ordinal))
        {
            return ResultGridToolbarAction.CopyRowValues;
        }

        return ResultGridToolbarAction.None;
    }

    public bool RequiresTableReader(ResultGridToolbarAction action)
    {
        return action != ResultGridToolbarAction.None;
    }
}
