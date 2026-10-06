using Avalonia.Controls.Templates;
using JustyBase.Behaviors;
using JustyBase.Models;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

/// <summary>
/// Column construction for the results grid: rebuilds <see cref="DataGrid.Columns"/> from
/// <see cref="CurrentResultsTable"/> and supplies the header template that combines the
/// drag source, the pin toggle and the summary menu. Pure rules live in
/// <see cref="IResultGridColumnReorderService"/> and <see cref="ResultGridColumnFactory"/>.
/// </summary>
public sealed partial class SqlResultsView
{
    private bool _refreshingColumns;

    private void DataGrid_Initialized(object? sender, System.EventArgs e)
    {
        RefreshDataGridColumns();
    }

    /// <summary>
    /// Refreshes DataGrid columns when data changes. Called from DataGrid_Initialized and OnCurrentResultsTableChanged.
    /// </summary>
    internal void RefreshDataGridColumns()
    {
        if (_refreshingColumns) return;
        if (CurrentResultsTable is null || ResultDataGrid is null || CurrentResultsTable.Headers.Count == 0)
        {
            return;
        }

        _refreshingColumns = true;
        try
        {
            // Clear existing columns to handle both new and recycled views
            ResultDataGrid.Columns.Clear();
            _pinnedColumns.Clear();
            _summaryScrollService.InvalidateRowHeaderWidthCache();

            // Update autocomplete items
            if (columnAutoComplet is not null)
            {
                List<string> headersListCopy = new(CurrentResultsTable.Headers);
                headersListCopy.Sort();
                columnAutoComplet.ItemsSource = headersListCopy;
            }

            // Recreate columns
            List<IValueConverter> valueConverters = [];
            for (var i = 0; i < CurrentResultsTable.Headers.Count; ++i)
            {
                FuncDataTemplate<object> headerTemplate = GetHeaderTemplate(CurrentResultsTable, i, i);

                DataGridBoundColumn col = ResultGridColumnFactory.CreateColumn(CurrentResultsTable, i, headerTemplate, _pinnedColumns, valueConverters);
                if (col.FilterFlyout is CascadingDistinctValueFilterFlyout filterFlyout)
                {
                    filterFlyout.SortRequested += direction => ApplyColumnSort(col, direction);
                }
                ResultDataGrid.Columns.Add(col);
            }
            ResultDataGrid.FrozenColumnCount = _pinnedColumns.Count;
        }
        finally
        {
            _refreshingColumns = false;
        }
    }

    private readonly Dictionary<string, int> _pinnedColumns = [];

    // This payload is consumed only by JustyBase. An application format keeps it
    // available to the in-process drop target on every Avalonia platform.
    private readonly DataFormat<string> _columnNameDataFormat =
        DataFormat.CreateStringApplicationFormat("JustyBase.ColumnName");

    private FuncDataTemplate<object> GetHeaderTemplate(TableOfSqlResults table, int index, int savedI)
    {
        return new FuncDataTemplate<object>((_, _) =>
        {
            var ctx = new ColumnHeaderContext
            {
                ColumnNameDataFormat = _columnNameDataFormat,
                PinnedColumns = _pinnedColumns,
                DataGrid = ResultDataGrid,
                ReorderService = _columnReorderService,
                PinIcon = this.Resources["btPinData"] as StreamGeometry ?? throw new InvalidOperationException("btPinData resource not found"),
                UnpinIcon = this.Resources["btPinData2"] as StreamGeometry ?? throw new InvalidOperationException("btPinData2 resource not found"),
                ViewModel = DataContext as SqlResultsViewModel,
                RefreshSummaryRowWidths = RefreshSummaryRowWidths,
                SavedIndex = savedI
            };
            return ColumnHeaderFactory.CreateHeaderControl(table, index, ctx);
        });
    }
}
