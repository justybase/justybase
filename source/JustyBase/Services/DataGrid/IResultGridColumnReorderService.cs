namespace JustyBase.Services.DataGrid;

/// <summary>
/// Pure rules for reordering DataGrid columns by dragging one header onto another.
/// Extracted from the column-header drop handler so the index math can be unit tested.
/// </summary>
public interface IResultGridColumnReorderService
{
    /// <summary>
    /// False when either header is missing/blank or when source and target are the same
    /// column — those drops must be ignored instead of reshuffling the grid.
    /// </summary>
    bool CanReorderHeaders(string? sourceColumnHeader, string? targetColumnHeader);

    /// <summary>
    /// Computes the <see cref="Avalonia.Controls.DataGridColumn"/>.DisplayIndex the dragged
    /// column should take when dropped before/after the target column.
    /// </summary>
    int CalculateNewDisplayIndex(int sourceDisplayIndex, int targetDisplayIndex, bool insertAfter, int columnCount);
}
