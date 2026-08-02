namespace JustyBase.Models;

/// <summary>
/// Represents a subtotal row displayed at the bottom of a group in the DataGrid.
/// </summary>
public sealed class GroupSubtotalRow
{
    /// <summary>
    /// The key/value of the group this subtotal belongs to.
    /// </summary>
    public object GroupKey { get; init; }

    /// <summary>
    /// The fields array, matching column structure. Contains summary values or empty.
    /// </summary>
    public object[] Fields { get; init; }

    /// <summary>
    /// Dictionary of column index to formatted summary string (e.g., "Σ 1,234.56").
    /// </summary>
    public Dictionary<int, string> SummaryValues { get; init; }

    /// <summary>
    /// Always true for this type, used for styling differentiation.
    /// </summary>
    public bool IsSubtotalRow => true;

    public GroupSubtotalRow()
    {
        Fields = [];
        SummaryValues = [];
    }

    public override string ToString()
    {
        return $"[Subtotal for {GroupKey}]";
    }
}
