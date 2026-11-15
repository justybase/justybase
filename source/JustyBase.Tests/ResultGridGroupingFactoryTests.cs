using System.ComponentModel;
using System.Globalization;
using Avalonia.Collections;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

/// <summary>
/// Group keys for the results grid must come from the column value, and NULL must be
/// its own single group. The regression these cover: <see cref="DataGridPathGroupDescription"/>
/// resolves <c>Fields[0]</c> through property-path reflection, and that API is
/// <c>[RequiresUnreferencedCode]</c>. In the Native AOT Windows build the lookup is
/// trimmed away, the path yields null, the grid falls back to using the row as the key,
/// every row collapses into one group and the header renders
/// <see cref="TableRow.EMPTY_NAME_PLACEHOLDED"/> as <c>&lt;NULL&gt;</c>.
/// </summary>
public sealed class ResultGridGroupingFactoryTests
{
    private static TableRow Row(params object?[] fields) => new() { Fields = fields };

    private static TableOfSqlResults TableWithHeaders(params string[] headers)
    {
        var table = new TableOfSqlResults();
        table.Headers.AddRange(headers);
        table.TypeCodes.AddRange(Enumerable.Repeat(TypeCode.Int32, headers.Length));
        return table;
    }

    [Fact]
    public void GetGroupKeyValue_ReturnsColumnValue()
    {
        Assert.Equal(2, ResultGridGroupingFactory.GetGroupKeyValue(Row(2, "a"), 0));
        Assert.Equal("a", ResultGridGroupingFactory.GetGroupKeyValue(Row(2, "a"), 1));
    }

    [Fact]
    public void GetGroupKeyValue_MapsNullAndDbNullToSharedSentinel()
    {
        Assert.Same(NullResultGridGroupKey.Instance, ResultGridGroupingFactory.GetGroupKeyValue(Row((object?)null), 0));
        Assert.Same(NullResultGridGroupKey.Instance, ResultGridGroupingFactory.GetGroupKeyValue(Row(DBNull.Value), 0));
        Assert.Equal("<NULL>", NullResultGridGroupKey.Instance.ToString());
    }

    [Fact]
    public void GetGroupKeyValue_OutOfRangeIndex_IsNullGroup()
    {
        Assert.Same(NullResultGridGroupKey.Instance, ResultGridGroupingFactory.GetGroupKeyValue(Row(), 3));
        Assert.Same(NullResultGridGroupKey.Instance, ResultGridGroupingFactory.GetGroupKeyValue(null, 0));
    }

    [Fact]
    public void CreateGroupDescription_ProducesDistinctKeysPerValue()
    {
        var description = ResultGridGroupingFactory.CreateGroupDescription(TableWithHeaders("ACCOUNTKEY"), 0);

        Assert.NotNull(description);
        Assert.Equal("Fields[0]", description.PropertyName);

        var rows = new[] { Row(2), Row(2), Row(3), Row(4) };
        var keys = rows
            .Select(row => description.GroupKeyFromItem(row, 0, CultureInfo.InvariantCulture))
            .Select(key => key?.ToString() ?? "<null>")
            .ToList();

        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("<NULL>", keys);
        Assert.DoesNotContain(TableRow.EMPTY_NAME_PLACEHOLDED, keys);
        Assert.Equal(["2", "2", "3", "4"], keys);
    }

    [Fact]
    public void CreateGroupDescription_CollapsesNullsIntoSingleGroup()
    {
        var description = ResultGridGroupingFactory.CreateGroupDescription(TableWithHeaders("ACCOUNTKEY"), 0);

        Assert.NotNull(description);

        var keys = new[] { Row(2), Row((object?)null), Row(3), Row(DBNull.Value) }
            .Select(row => description.GroupKeyFromItem(row, 0, CultureInfo.InvariantCulture))
            .ToList();

        var nullKeys = keys.Where(key => key is NullResultGridGroupKey).ToList();
        Assert.Equal(2, nullKeys.Count);
        Assert.All(nullKeys, key => Assert.Same(NullResultGridGroupKey.Instance, key));
        Assert.Equal(2, keys.Count(key => key is not NullResultGridGroupKey));
    }

    [Fact]
    public void CreateGroupDescription_RejectsColumnOutsideTable()
    {
        var table = TableWithHeaders("ACCOUNTKEY");

        Assert.Null(ResultGridGroupingFactory.CreateGroupDescription(table, -1));
        Assert.Null(ResultGridGroupingFactory.CreateGroupDescription(table, 1));
    }

    [Fact]
    public void CreateSortDescription_SortsByColumnValue()
    {
        var table = TableWithHeaders("ACCOUNTKEY");
        var description = ResultGridGroupingFactory.CreateSortDescription(table, 0, ListSortDirection.Ascending);

        Assert.NotNull(description);
        Assert.Equal(ListSortDirection.Ascending, description.Direction);

        var sorted = description.OrderBy(new object[] { Row(3), Row(2), Row(4) }).Cast<TableRow>().ToList();

        Assert.Equal([2, 3, 4], sorted.Select(row => (int)row.Fields[0]!));
    }

    [Fact]
    public void CreateSortDescription_RejectsColumnOutsideTable()
    {
        var table = TableWithHeaders("ACCOUNTKEY");

        Assert.Null(ResultGridGroupingFactory.CreateSortDescription(table, -1, ListSortDirection.Ascending));
        Assert.Null(ResultGridGroupingFactory.CreateSortDescription(table, 1, ListSortDirection.Ascending));
    }

    [Fact]
    public void ResolveColumnIndex_ParsesGroupPropertyName()
    {
        Assert.Equal(0, ResultGridGroupingFactory.ResolveColumnIndex("Fields[0]"));
        Assert.Equal(42, ResultGridGroupingFactory.ResolveColumnIndex("Fields[42]"));
        Assert.Equal(-1, ResultGridGroupingFactory.ResolveColumnIndex("NAME"));
        Assert.Equal(-1, ResultGridGroupingFactory.ResolveColumnIndex(null));
    }
}
