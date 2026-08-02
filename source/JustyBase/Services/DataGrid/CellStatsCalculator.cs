namespace JustyBase.Services.DataGrid;

/// <summary>
/// Result of cell stats calculation (Avalonia UI shape).
/// </summary>
public sealed class CellStatsResult
{
    public int SelectedCount { get; init; }
    public decimal Sum { get; init; }
    public int NotNullCount { get; init; }
    public int DistinctCount { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public List<object> SelectedValues { get; init; } = [];

    public string ToDisplayString()
    {
        string minText = Min.HasValue ? Min.Value.ToString("N3") : "-";
        string maxText = Max.HasValue ? Max.Value.ToString("N3") : "-";
        return $"Selected {SelectedCount:N0} cells | Sum {Sum:N3} | Count {NotNullCount:N0} | Distinct {DistinctCount:N0} | Min {minText} | Max {maxText}";
    }
}

/// <summary>
/// Host adapter over <see cref="JustyBase.Core.Grid.CellStatsCalculator"/>.
/// </summary>
public sealed class CellStatsCalculator
{
    public CellStatsResult Calculate(IReadOnlyList<(object? Value, TypeCode TypeCode)> cellValues)
    {
        var core = JustyBase.Core.Grid.CellStatsCalculator.Calculate(cellValues);
        var selectedValues = new List<object>(cellValues.Count);
        foreach (var (value, _) in cellValues)
        {
            if (value is not null)
                selectedValues.Add(value);
        }

        return new CellStatsResult
        {
            SelectedCount = core.Count,
            Sum = core.Sum ?? 0m,
            NotNullCount = core.Count - core.NullCount,
            DistinctCount = core.DistinctCount,
            Min = core.Minimum,
            Max = core.Maximum,
            SelectedValues = selectedValues
        };
    }

    /// <summary>
    /// Attempts to convert a cell value to a decimal based on its TypeCode.
    /// Kept for callers that need the typed conversion without full stats.
    /// </summary>
    public static bool TryConvertNumericCellValue(object value, TypeCode typeCode, out decimal numericValue)
    {
        var stats = JustyBase.Core.Grid.CellStatsCalculator.Calculate([(value, typeCode)]);
        if (stats.NumericCount == 0 || stats.Sum is null)
        {
            numericValue = 0;
            return false;
        }

        numericValue = stats.Sum.Value;
        return true;
    }
}
