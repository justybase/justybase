using System.Globalization;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Presentation rules for result-grid row and group headers. Extracted from
/// <c>SqlResultsView.DataGrid_LoadingRow</c> / <c>DataGrid_LoadingRowGroup</c> so the numbering
/// and striping decisions can be unit tested without virtualized rows.
/// </summary>
public static class ResultGridRowHeaderRules
{
    /// <summary>Item counter shown inside a grouped-row header.</summary>
    public const string GroupItemCountFormat = "({0:N0} Items)";

    /// <summary>
    /// Row numbers are 1-based and grouped according to the active culture
    /// (e.g. index 12344 renders as <c>12,345</c> in the invariant culture).
    /// </summary>
    public static string GetRowHeaderText(int rowIndex, CultureInfo? culture = null)
        => (rowIndex + 1).ToString("N0", culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Stripe by data index — :nth-child breaks under row virtualization/recycling.
    /// </summary>
    public static bool IsOddRow(int rowIndex)
        => rowIndex % 2 == 1;
}
