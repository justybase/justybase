using JustyBase.Models;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services.DataGrid;

public sealed class ResultGridDoubleTapService : IResultGridDoubleTapService
{
    public bool ShouldHandleHeaderDoubleTap(
        bool isHeaderClicked,
        bool isRowDetailsGrid,
        bool sourceIsSqlResultsViewModel,
        bool sourceIsDataGridCollectionViewGroup)
    {
        return isHeaderClicked
            && !isRowDetailsGrid
            && !sourceIsSqlResultsViewModel
            && !sourceIsDataGridCollectionViewGroup;
    }

    public object? GetHeaderDoubleTapValue(object? sourceDataContext)
    {
        return sourceDataContext?.ToString();
    }

    public object? GetTableRowDoubleTapValue(TableRow row, int columnIndex)
    {
        return row.Fields[columnIndex];
    }

    public ResultGridDoubleTapPayload GetRowDetailDoubleTapPayload(RowDetail rowDetail, int currentColumnDisplayIndex, int columnsCount)
    {
        if (currentColumnDisplayIndex == 0)
        {
            return new ResultGridDoubleTapPayload(rowDetail.Name, true);
        }

        if (currentColumnDisplayIndex < columnsCount - 1)
        {
            return new ResultGridDoubleTapPayload(rowDetail.FieldsValues[currentColumnDisplayIndex - 1], false);
        }

        return new ResultGridDoubleTapPayload(rowDetail.TypeName, false);
    }
}
