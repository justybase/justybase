using System.ComponentModel;
using System.Globalization;
using Avalonia.Collections;
using Avalonia.Controls.DataGridFiltering;
using JustyBase.Converters;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.Services.DataGrid;
using JustyBase.Services.Results;

namespace JustyBase.ViewModels.Tools;

/// <summary>
/// Grid-interaction logic formerly living in <c>SqlResultsView.axaml.cs</c>:
/// grouping, filter-search and selection statistics. The view only translates
/// control events into these calls.
/// </summary>
public sealed partial class SqlResultsViewModel
{
    // ------------------------------------------------------------------
    // Grouping
    // ------------------------------------------------------------------

    /// <summary>
    /// Adds or removes a grouping level for <paramref name="columnName"/> and refreshes
    /// the grouped-columns state plus the summary row.
    /// </summary>
    public void ToggleGroupByColumn(string columnName, IResultGridGroupingService groupingService)
    {
        if (GridCollectionView is null || CurrentResultsTable is null)
        {
            return;
        }

        // Grouping walks every item, so a paged spill cannot be grouped in
        // place. Materialize the filtered result when the shared budget allows
        // it; otherwise refuse instead of silently grouping one page.
        if (IsSpillMode && !TryMaterializeSpillForGrouping())
        {
            return;
        }

        if (GridCollectionView.Count >= 1_000_000)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("Too many items to group");
            return;
        }

        var groupedPropertyNames = GridCollectionView.GroupDescriptions
            .Select(static gd => gd.PropertyName)
            .ToList();
        var togglePlan = groupingService.BuildTogglePlan(columnName, CurrentResultsTable.Headers, groupedPropertyNames);
        if (togglePlan.Action == GroupingToggleAction.None)
        {
            return;
        }

        if (togglePlan.Action == GroupingToggleAction.Remove)
        {
            if ((uint)togglePlan.ExistingIndex < (uint)GridCollectionView.GroupDescriptions.Count)
            {
                GridCollectionView.GroupDescriptions.RemoveAt(togglePlan.ExistingIndex);
            }
        }
        else
        {
            // Add sort description for grouping - DataGridCollectionView will handle sorting automatically
            var dataGridSortDescription = DataGridSortDescription.FromPath(togglePlan.PropertyName, ListSortDirection.Ascending);
            GridCollectionView.SortDescriptions.Add(dataGridSortDescription);

            var group = new DataGridPathGroupDescription(togglePlan.PropertyName)
            {
                ValueConverter = new ForGroupValueConverter()
            };
            GridCollectionView.GroupDescriptions.Add(group);
        }

        RefreshGroupedColumnsState(groupingService);

        // Post to dispatcher so the DataGrid layout updates first (headers shifting).
        Dispatcher.UIThread.Post(
            () => ViewBridge?.RecalculateSummaryValues(),
            DispatcherPriority.Input);
    }

    /// <summary>
    /// Reorders an existing grouping level. Called from the grouped-column reorder behavior.
    /// </summary>
    public void MoveGroup(string sourceColName, string targetColName, IResultGridGroupingService groupingService)
    {
        if (GridCollectionView is null || CurrentResultsTable is null)
        {
            return;
        }

        var groupedPropertyNames = GridCollectionView.GroupDescriptions
            .Select(static gd => gd.PropertyName)
            .ToList();
        if (!groupingService.TryFindMoveIndexes(
                groupedPropertyNames,
                CurrentResultsTable.Headers,
                sourceColName,
                targetColName,
                out int sourceIndex,
                out int targetIndex))
        {
            return;
        }

        var item = GridCollectionView.GroupDescriptions[sourceIndex];
        GridCollectionView.GroupDescriptions.RemoveAt(sourceIndex);
        GridCollectionView.GroupDescriptions.Insert(targetIndex, item);

        RefreshGroupedColumnsState(groupingService);

        Dispatcher.UIThread.Post(
            () => ViewBridge?.RecalculateSummaryValues(),
            DispatcherPriority.Input);
    }

    private void RefreshGroupedColumnsState(IResultGridGroupingService groupingService)
    {
        GroupedColumns.Clear();
        if (GridCollectionView is null || CurrentResultsTable is null)
        {
            return;
        }

        var groupedPropertyNames = GridCollectionView.GroupDescriptions
            .Select(static gd => gd.PropertyName)
            .ToList();
        foreach (var groupedColumn in groupingService.ToGroupedColumnNames(groupedPropertyNames, CurrentResultsTable.Headers))
        {
            GroupedColumns.Add(groupedColumn);
        }
    }

    // ------------------------------------------------------------------
    // Filter / search
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies the row filter for the current <see cref="SearchText"/>/<see cref="ContainsGeneralSearch"/>
    /// and rebuilds the collection view. Scheduled from the view's debounce timer.
    /// </summary>
    public void ApplyFilterSearch(IResultGridSearchService searchService)
    {
        if (SearchInProgress || CurrentResultsTable is null || CurrentResultsTable.Headers.Count <= 0)
        {
            return;
        }

        if (IsSpillMode)
        {
            ApplySpillFilter();
            return;
        }

        if (CurrentResultsTable.Rows is null || CurrentResultsTable.Rows.Count <= 0)
        {
            return;
        }

        SearchInProgress = true;
        // Capture before SuspendGridBinding detaches ItemsSource (which clears selection).
        bool clearLargeSelection = SelectedItems?.Count > 5_000;
        try
        {
            // Detach so FilteredRows mutations do not layout against a live DataGrid.
            ViewBridge?.SuspendGridBinding();

            searchService.ApplySearch(CurrentResultsTable, SearchText, null, ContainsGeneralSearch);

            if (clearLargeSelection && SelectedItems is not null && SelectedItems.Count > 0)
            {
                SelectedItems.Clear();
            }

            GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);
            RowsLoadingMessage = $"{GridCollectionView.Count:N0} rows";
            ViewBridge?.RecalculateSummaryValues();
            RefreshFind();
            ViewBridge?.InvalidateSummaryLayout();
        }
        finally
        {
            ViewBridge?.ResumeGridBinding(clearSelection: clearLargeSelection);
            SearchInProgress = false;
        }
    }

    /// <summary>
    /// Re-applies the grid's column filters to the spilled result in SQLite.
    /// Called by the view when <see cref="FilteringModel"/> changes; without this,
    /// a column filter would only hide rows on the currently visible page.
    /// </summary>
    public void ApplySpillColumnFiltering()
    {
        if (!IsSpillMode || _spillStore is null || _applyingSpillFilter)
        {
            return;
        }

        _applyingSpillFilter = true;
        try
        {
            ApplySpillFilter();
        }
        finally
        {
            _applyingSpillFilter = false;
        }
    }

    /// <summary>
    /// Runs the grid search and the column filters against the spilled result in
    /// SQLite, then loads the first matching page. This keeps filtering a
    /// multi-million-row result from touching only the page that happens to be
    /// visible.
    /// </summary>
    private void ApplySpillFilter()
    {
        if (_spillStore is null)
        {
            return;
        }

        SearchInProgress = true;
        bool clearLargeSelection = SelectedItems?.Count > 5_000;
        try
        {
            ViewBridge?.SuspendGridBinding();

            _spillFilter = BuildSpillFilter();
            SpillFilteredRows = _spillStore.SetFilter(_spillFilter);
            ApplySpillPage(0);

            if (clearLargeSelection && SelectedItems is not null && SelectedItems.Count > 0)
            {
                SelectedItems.Clear();
            }

            GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);
            RowsLoadingMessage = BuildSpillStatusMessage();
            ViewBridge?.RecalculateSummaryValues();
            RefreshFind();
            ViewBridge?.InvalidateSummaryLayout();
        }
        finally
        {
            ViewBridge?.ResumeGridBinding(clearSelection: clearLargeSelection);
            SearchInProgress = false;
            NotifySpillCommands();
        }
    }

    private SpillFilter BuildSpillFilter()
    {
        var columnFilters = new List<SpillColumnFilter>();
        foreach (FilteringDescriptor descriptor in FilteringModel.Descriptors)
        {
            int index = ResolveSpillColumnIndex(descriptor);
            if (index < 0)
            {
                continue;
            }

            switch (descriptor.Operator)
            {
                case FilteringOperator.Equals:
                    columnFilters.Add(descriptor.Value is null
                        ? new SpillColumnFilter(index, SpillFilterOperator.IsNull)
                        : new SpillColumnFilter(index, SpillFilterOperator.Equal, descriptor.Value));
                    break;
                case FilteringOperator.NotEquals:
                    columnFilters.Add(descriptor.Value is null
                        ? new SpillColumnFilter(index, SpillFilterOperator.IsNotNull)
                        : new SpillColumnFilter(index, SpillFilterOperator.NotEqual, descriptor.Value));
                    break;
                case FilteringOperator.Contains:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.Contains, descriptor.Value));
                    break;
                case FilteringOperator.StartsWith:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.StartsWith, descriptor.Value));
                    break;
                case FilteringOperator.EndsWith:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.EndsWith, descriptor.Value));
                    break;
                case FilteringOperator.GreaterThan:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.GreaterThan, descriptor.Value));
                    break;
                case FilteringOperator.GreaterThanOrEqual:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.GreaterThanOrEqual, descriptor.Value));
                    break;
                case FilteringOperator.LessThan:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.LessThan, descriptor.Value));
                    break;
                case FilteringOperator.LessThanOrEqual:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.LessThanOrEqual, descriptor.Value));
                    break;
                case FilteringOperator.Between when descriptor.Values is { Count: >= 2 }:
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.GreaterThanOrEqual, descriptor.Values[0]));
                    columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.LessThanOrEqual, descriptor.Values[1]));
                    break;
                case FilteringOperator.In when descriptor.Values is { Count: > 0 }:
                    bool includeNull = descriptor.Values.Any(static value => value is null);
                    object[] values = descriptor.Values.Where(static value => value is not null).ToArray();
                    if (values.Length == 0)
                    {
                        columnFilters.Add(new SpillColumnFilter(index, SpillFilterOperator.IsNull));
                    }
                    else
                    {
                        columnFilters.Add(new SpillColumnFilter(
                            index,
                            SpillFilterOperator.In,
                            Values: values,
                            IncludeNull: includeNull));
                    }

                    break;
            }
        }

        return new SpillFilter(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            ContainsGeneralSearch,
            columnFilters);
    }

    private int ResolveSpillColumnIndex(FilteringDescriptor descriptor)
    {
        const string prefix = "Fields[";
        string? path = descriptor.PropertyPath;
        if (path is not null)
        {
            int start = path.IndexOf(prefix, StringComparison.Ordinal);
            if (start >= 0)
            {
                int end = path.IndexOf(']', start + prefix.Length);
                if (end > start
                    && int.TryParse(
                        path.AsSpan(start + prefix.Length, end - start - prefix.Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int index)
                    && index >= 0
                    && index < CurrentResultsTable.Headers.Count)
                {
                    return index;
                }
            }
        }

        if (descriptor.ColumnId is string columnId)
        {
            int index = CurrentResultsTable.Headers.IndexOf(columnId);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------
    // Sorting
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies a single-column custom sort to the underlying table and rebuilds the view.
    /// The DataGrid column visuals (sort glyphs) stay in the view.
    /// </summary>
    internal void ApplyColumnSort(CustomResultComparer comparer, ListSortDirection direction)
    {
        if (CurrentResultsTable is null)
        {
            return;
        }

        if (IsSpillMode && _spillStore is not null)
        {
            _spillSort = [new SpillSortKey(comparer.Index, direction == ListSortDirection.Descending)];
            ViewBridge?.SuspendGridBinding();
            try
            {
                ApplySpillPage(0);
                GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);
                RowsLoadingMessage = BuildSpillStatusMessage();
            }
            finally
            {
                ViewBridge?.ResumeGridBinding();
            }

            return;
        }

        CurrentResultsTable.ColumnsToSort.Clear();
        CurrentResultsTable.ColumnsToSort.Add(new TableOfSqlResults.SortInfo
        {
            ColNumber = comparer.Index,
            SortDirection = direction,
            Comparer = comparer
        });

        ViewBridge?.SuspendGridBinding();
        try
        {
            CurrentResultsTable.SortFilteredRows();
            GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);
        }
        finally
        {
            ViewBridge?.ResumeGridBinding();
        }
    }

    // ------------------------------------------------------------------
    // Selection statistics
    // ------------------------------------------------------------------

    /// <summary>
    /// Updates the status-bar summary for the current cell/row selection.
    /// </summary>
    public void UpdateSelectionStats(IReadOnlyList<DataGridCellInfo> selectedCells, IResultGridStatsService statsService)
    {
        var currentResultsTable = CurrentResultsTable;
        if (currentResultsTable is null)
        {
            SelectedColumnCells = [];
            StatsText = "Selected 0 cells | Sum 0.000 | Count 0 | Distinct 0 | Min - | Max -";
            return;
        }

        var result = statsService.CalculateStats(selectedCells, currentResultsTable);
        SelectedColumnCells = result.SelectedValues;
        StatsText = result.ToDisplayString();
    }

    /// <summary>Status text for a single selected cell (no numeric aggregation).</summary>
    public void SetSingleCellSelectionSummary()
    {
        SelectedColumnCells = [];
        StatsText = "Selected 1 cell";
    }
}
