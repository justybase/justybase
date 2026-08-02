using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

public sealed class ColumnHeaderInfo
{
    public required string ColumnName { get; init; }
    public required int ColumnIndex { get; init; }
    public required bool IsNumeric { get; init; }
    public bool IsPinned { get; set; }
    public bool HasFilter { get; set; }
    public bool HasSummary { get; set; }
}

public sealed class ColumnHeaderCallbacks
{
    public Action<string>? OnPinToggle { get; init; }
    public Action<int, ColumnSummaryType>? OnSummaryTypeChange { get; init; }
    public Action<int>? OnFilterOpen { get; init; }
    public Action<string>? OnColumnDragStart { get; init; }
    public Action<string, string, bool>? OnColumnReorder { get; init; }
    public Func<int, bool>? IsColumnPinned { get; init; }
    public Func<int, bool>? HasColumnFilter { get; init; }
    public Func<int, ColumnSummaryType>? GetColumnSummaryType { get; init; }
}

public sealed class SummaryMenuOptions
{
    public static readonly IReadOnlyList<(string DisplayText, ColumnSummaryType Type)> NumericOptions =
    [
        ("Σ Sum", ColumnSummaryType.Sum),
        ("# Count", ColumnSummaryType.Count),
        ("Ø Average", ColumnSummaryType.Average),
        ("↓ Min", ColumnSummaryType.Min),
        ("↑ Max", ColumnSummaryType.Max),
        ("≠ Distinct", ColumnSummaryType.Distinct)
    ];

    public static readonly IReadOnlyList<(string DisplayText, ColumnSummaryType Type)> NonNumericOptions =
    [
        ("# Count", ColumnSummaryType.Count),
        ("≠ Distinct", ColumnSummaryType.Distinct)
    ];

    public static readonly (string DisplayText, ColumnSummaryType Type) NoneOption = ("✕ None (Remove)", ColumnSummaryType.None);
}
