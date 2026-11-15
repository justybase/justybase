namespace JustyBase.Services.Results;

/// <summary>
/// Estimates the managed memory a decoded result row keeps alive. The value is
/// deliberately approximate: it only needs to be stable enough for the shared
/// <see cref="ResultMemoryBudget"/> to make spill decisions.
/// </summary>
public static class ResultRowSizer
{
    private const int TableRowOverhead = 40;
    private const int ArrayOverhead = 24;
    private const int ReferenceSize = 8;
    private const int BoxedValueOverhead = 24;

    public static long Estimate(object?[] fields)
    {
        long total = TableRowOverhead + ArrayOverhead + ((long)fields.Length * ReferenceSize);
        foreach (object? value in fields)
        {
            total += EstimateValue(value);
        }

        return total;
    }

    public static long EstimateValue(object? value)
        => value switch
        {
            null or DBNull => ReferenceSize,
            string text => BoxedValueOverhead + (2L * text.Length),
            byte[] bytes => BoxedValueOverhead + bytes.Length,
            _ => BoxedValueOverhead
        };
}
