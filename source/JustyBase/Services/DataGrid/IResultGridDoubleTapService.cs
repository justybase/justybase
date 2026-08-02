using JustyBase.Models;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services.DataGrid;

public readonly record struct ResultGridDoubleTapPayload(object? Value, bool RawMode);

public interface IResultGridDoubleTapService
{
    bool ShouldHandleHeaderDoubleTap(
        bool isHeaderClicked,
        bool isRowDetailsGrid,
        bool sourceIsSqlResultsViewModel,
        bool sourceIsDataGridCollectionViewGroup);

    object? GetHeaderDoubleTapValue(object? sourceDataContext);

    object? GetTableRowDoubleTapValue(TableRow row, int columnIndex);

    ResultGridDoubleTapPayload GetRowDetailDoubleTapPayload(RowDetail rowDetail, int currentColumnDisplayIndex, int columnsCount);
}
