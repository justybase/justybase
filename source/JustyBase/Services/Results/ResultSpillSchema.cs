using System.Data;
using JustyBase.Models;

namespace JustyBase.Services.Results;

/// <summary>Physical storage used by a spilled result column.</summary>
public enum SpillColumnKind
{
    Integral,
    Boolean,
    Real,
    Numeric,
    DateTime,
    Text,
    Blob
}

/// <summary>
/// One result column and the SQLite representation chosen for it. The original
/// <see cref="TypeCode"/> is kept so page reads can restore the CLR type the
/// grid comparers expect.
/// </summary>
public sealed record ResultSpillColumn(string Name, TypeCode TypeCode, SpillColumnKind Kind)
{
    /// <summary>
    /// Decimals are stored as exact text plus a hidden REAL sort column, so
    /// ordering never silently truncates a <c>NUMERIC(38)</c> value.
    /// </summary>
    public bool NeedsSortColumn => Kind == SpillColumnKind.Numeric;
}

/// <summary>
/// Typed schema for <see cref="ResultSpillStore"/>. Building it from the result
/// table keeps the SQLite file self-describing (one typed column per result
/// column) instead of storing every row as one JSON payload.
/// </summary>
public sealed class ResultSpillSchema
{
    public ResultSpillSchema(IReadOnlyList<ResultSpillColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new ArgumentException("A spill schema needs at least one column.", nameof(columns));
        }

        Columns = columns;
    }

    public IReadOnlyList<ResultSpillColumn> Columns { get; }

    public static ResultSpillSchema FromTable(TableOfSqlResults table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = new List<ResultSpillColumn>(table.Headers.Count);
        for (int i = 0; i < table.Headers.Count; i++)
        {
            TypeCode typeCode = i < table.TypeCodes.Count ? table.TypeCodes[i] : TypeCode.String;
            string? typeName = i < table.DataTypeNames.Count ? table.DataTypeNames[i] : null;
            columns.Add(new ResultSpillColumn(
                table.Headers[i],
                typeCode,
                KindFor(typeCode, typeName)));
        }

        return new ResultSpillSchema(columns);
    }

    public static SpillColumnKind KindFor(TypeCode typeCode, string? dataTypeName)
    {
        switch (typeCode)
        {
            case TypeCode.Boolean:
                return SpillColumnKind.Boolean;
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
                return SpillColumnKind.Integral;
            case TypeCode.UInt64:
                // UInt64 can exceed Int64.MaxValue; keep the exact decimal text.
                return SpillColumnKind.Numeric;
            case TypeCode.Single:
            case TypeCode.Double:
                return SpillColumnKind.Real;
            case TypeCode.Decimal:
                return SpillColumnKind.Numeric;
            case TypeCode.DateTime:
                return SpillColumnKind.DateTime;
            case TypeCode.Char:
            case TypeCode.String:
                return SpillColumnKind.Text;
            default:
                return LooksLikeBinary(dataTypeName) ? SpillColumnKind.Blob : SpillColumnKind.Text;
        }
    }

    private static bool LooksLikeBinary(string? dataTypeName)
    {
        if (string.IsNullOrWhiteSpace(dataTypeName))
        {
            return false;
        }

        return dataTypeName.Contains("bytea", StringComparison.OrdinalIgnoreCase)
            || dataTypeName.Contains("binary", StringComparison.OrdinalIgnoreCase)
            || dataTypeName.Contains("blob", StringComparison.OrdinalIgnoreCase)
            || dataTypeName.Contains("image", StringComparison.OrdinalIgnoreCase)
            || dataTypeName.Contains("varbinary", StringComparison.OrdinalIgnoreCase)
            || dataTypeName.Contains("raw", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One ORDER BY term for a spilled result.</summary>
public sealed record SpillSortKey(int ColumnIndex, bool Descending);

public enum SpillFilterOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    In,
    IsNull,
    IsNotNull
}

/// <summary>Column predicate translated into a SQLite WHERE term.</summary>
public sealed record SpillColumnFilter(
    int ColumnIndex,
    SpillFilterOperator Operator,
    object? Value = null,
    IReadOnlyList<object?>? Values = null,
    bool IncludeNull = false);

/// <summary>
/// Row filter for a spilled result. <see cref="SearchText"/> mirrors the grid
/// search box; <see cref="ColumnFilters"/> carries typed column predicates.
/// </summary>
public sealed record SpillFilter(
    string? SearchText,
    bool ContainsSearch,
    IReadOnlyList<SpillColumnFilter> ColumnFilters)
{
    public static SpillFilter Empty { get; } = new(null, true, []);

    public bool IsEmpty => string.IsNullOrWhiteSpace(SearchText) && ColumnFilters.Count == 0;
}

/// <summary>Exception raised when the shared spill quota is exhausted.</summary>
public sealed class SpillQuotaExceededException : Exception
{
    public SpillQuotaExceededException(long quotaBytes, long usedBytes, long requestedBytes)
        : base($"Result spill quota exceeded ({quotaBytes:N0} bytes; {usedBytes:N0} used, {requestedBytes:N0} requested).")
    {
        QuotaBytes = quotaBytes;
        UsedBytes = usedBytes;
        RequestedBytes = requestedBytes;
    }

    public long QuotaBytes { get; }

    public long UsedBytes { get; }

    public long RequestedBytes { get; }
}
