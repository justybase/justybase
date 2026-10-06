using System.ComponentModel;
using JustyBase.Helpers;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Sorting rules applied when a result-grid header is clicked. Extracted from
/// <c>SqlResultsView.ResultDataGrid_Sorting</c> so the direction/comparer decisions can be
/// unit tested without a live DataGrid.
/// </summary>
public static class ResultGridColumnSortRules
{
    /// <summary>
    /// Clicking an unsorted column starts ascending; clicking a sorted one flips it.
    /// </summary>
    public static ListSortDirection NextDirection(ListSortDirection? currentDirection)
        => currentDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

    /// <summary>
    /// Only columns created by <see cref="ResultGridColumnFactory"/> — i.e. columns carrying a
    /// <see cref="CustomResultComparer"/> — take part in the custom sort. Any other comparer
    /// (or none at all) leaves the grid on its built-in sorting.
    /// </summary>
    public static bool CanApplyCustomSort(object? customSortComparer)
        => customSortComparer is CustomResultComparer;
}
