using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Collections;
using Avalonia.Controls;
using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Sentinel group key for NULL and <see cref="DBNull"/> cells. A single shared
/// instance keeps every NULL row in one group without depending on
/// <see cref="TableRow.ToString"/>, and labels that group <c>&lt;NULL&gt;</c>.
/// </summary>
public sealed class NullResultGridGroupKey
{
    /// <summary>The single NULL group key.</summary>
    public static NullResultGridGroupKey Instance { get; } = new();

    private NullResultGridGroupKey()
    {
    }

    public override string ToString() => "<NULL>";
}

/// <summary>
/// Compares result-grid group keys. Values compare with <see cref="object.Equals(object)"/>
/// so boxed numbers of the same value share a group; the NULL sentinel compares by identity.
/// </summary>
public sealed class ResultGridGroupKeyComparer : IEqualityComparer<object>
{
    public static ResultGridGroupKeyComparer Instance { get; } = new();

    private ResultGridGroupKeyComparer()
    {
    }

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y) || (x is not null && x.Equals(y));

    public int GetHashCode(object obj) => obj.GetHashCode();
}

/// <summary>
/// Orders rows for grouping by the grouped column's value.
/// </summary>
/// <remarks>
/// Deliberately independent of <c>TypeCodes</c>: the reported column type and the
/// runtime value can disagree (a provider may hand back an <see cref="int"/> for a
/// column declared as string, or a boxed <see cref="decimal"/> where the type code
/// says <c>Double</c>). A typed cast in that situation throws
/// <see cref="InvalidCastException"/> out of the collection view. NULL and
/// <see cref="DBNull"/> sort first, numbers compare numerically, and everything else
/// falls back to an ordinal/invariant comparison, so a mismatch degrades to a
/// slightly different order instead of a crash.
/// </remarks>
public sealed class ResultGridGroupKeySortComparer : IComparer<object>
{
    public static ResultGridGroupKeySortComparer Instance { get; } = new();

    private ResultGridGroupKeySortComparer()
    {
    }

    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || x is DBNull)
        {
            return -1;
        }

        if (y is null || y is DBNull)
        {
            return 1;
        }

        if (x.GetType() == y.GetType() && x is IComparable comparable)
        {
            return comparable.CompareTo(y);
        }

        return string.CompareOrdinal(
            Convert.ToString(x, CultureInfo.InvariantCulture),
            Convert.ToString(y, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Builds result-grid group and sort descriptions without property-path reflection.
/// </summary>
/// <remarks>
/// <see cref="DataGridPathGroupDescription"/> resolves <c>Fields[index]</c> through
/// <c>TypeHelper</c> property-path reflection and is annotated
/// <see cref="System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"/>.
/// In the Native AOT Windows build that lookup can be trimmed away: the path returns
/// null, the grid then uses the row itself as the group key, every row collapses into
/// one group and the header shows <see cref="TableRow.EMPTY_NAME_PLACEHOLDED"/>
/// (rendered as <c>&lt;NULL&gt;</c>). The generated description below reads the column
/// through a compiled delegate instead, which trimming cannot break.
/// </remarks>
public static class ResultGridGroupingFactory
{
    /// <summary>
    /// Creates the group description for <paramref name="columnIndex"/>, or null when
    /// the column is outside the table.
    /// </summary>
    public static DataGridGroupDescription? CreateGroupDescription(TableOfSqlResults table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.Headers.Count)
        {
            return null;
        }

        return new DataGridGeneratedGroupDescription<TableRow, object>(
            GroupPropertyNameHelper.CreatePropertyName(columnIndex),
            row => GetGroupKeyValue(row, columnIndex),
            ResultGridGroupKeyComparer.Instance);
    }

    /// <summary>
    /// Creates the sort description used to order the rows before grouping. Rows sort by
    /// the grouped column's runtime value (see <see cref="ResultGridGroupKeySortComparer"/>),
    /// so no reflection and no type-code cast is involved.
    /// </summary>
    public static DataGridSortDescription? CreateSortDescription(TableOfSqlResults table, int columnIndex, ListSortDirection direction)
    {
        if (columnIndex < 0 || columnIndex >= table.Headers.Count)
        {
            return null;
        }

        return DataGridSortDescription.FromComparer(
            new ResultGridRowColumnComparer(columnIndex),
            direction);
    }

    /// <summary>Compares two rows by one column's value.</summary>
    private sealed class ResultGridRowColumnComparer(int columnIndex) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not TableRow left || y is not TableRow right)
            {
                return 0;
            }

            return ResultGridGroupKeySortComparer.Instance.Compare(
                GetGroupKeyValue(left, columnIndex),
                GetGroupKeyValue(right, columnIndex));
        }
    }

    /// <summary>Group key for one row, mapping null and <see cref="DBNull"/> onto the shared NULL key.</summary>
    public static object GetGroupKeyValue(TableRow? row, int columnIndex)
    {
        if (row is null || columnIndex < 0 || columnIndex >= row.Fields.Length)
        {
            return NullResultGridGroupKey.Instance;
        }

        object? value = row.Fields[columnIndex];
        return value is null or DBNull ? NullResultGridGroupKey.Instance : value;
    }

    /// <summary>Column index encoded in a group property name (<c>Fields[index]</c>), or -1.</summary>
    public static int ResolveColumnIndex(string? propertyName)
    {
        return GroupPropertyNameHelper.TryExtractColumnIndex(propertyName, out int columnIndex) ? columnIndex : -1;
    }
}
