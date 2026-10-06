using System.ComponentModel;
using Avalonia.Collections;
using JustyBase.Converters;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

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
        if (SearchInProgress
            || CurrentResultsTable is null
            || CurrentResultsTable.Rows is null
            || CurrentResultsTable.Rows.Count <= 0
            || CurrentResultsTable.Headers.Count <= 0)
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
