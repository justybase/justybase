using JustyBase.Models;
using JustyBase.Services.DataGrid;
using System;
using System.Collections.Generic;

namespace JustyBase.Tests;

public sealed class ResultGridSearchServiceTests
{
    [Fact]
    public void ApplySearch_EmptySearchText_NoColumnFilters_ReturnsAllRows()
    {
        var table = CreateTable();
        var service = new ResultGridSearchService();

        int count = service.ApplySearch(table, "", additionalValues: null, containsGeneralSearch: false);

        Assert.Equal(3, count);
        Assert.Equal(3, table.FilteredRows.Count);
    }

    [Fact]
    public void ApplySearch_StringSearch_ExactMatch_WhenGeneralSearchIsFalse()
    {
        var table = CreateTable();
        var service = new ResultGridSearchService();

        int count = service.ApplySearch(table, "abc", additionalValues: null, containsGeneralSearch: false);

        Assert.Equal(1, count);
        Assert.Equal("abc", table.FilteredRows[0].Fields[0]);
    }

    [Fact]
    public void ApplySearch_StringSearch_ContainsMatch_WhenGeneralSearchIsTrue()
    {
        var table = CreateTable();
        var service = new ResultGridSearchService();

        int count = service.ApplySearch(table, "abc", additionalValues: null, containsGeneralSearch: true);

        Assert.Equal(2, count);
    }

    [Fact]
    public void ApplySearch_ColumnFilter_IsApplied()
    {
        var table = CreateTable();
        var service = new ResultGridSearchService();

        var filter = new AditionalOneFilter("5") { FilterType = FilterTypeEnum.greaterThan };
        var additionalValues = new Dictionary<int, AditionalOneFilter> { [1] = filter };

        int count = service.ApplySearch(table, searchText: "", additionalValues, containsGeneralSearch: false);

        Assert.Equal(2, count);
        Assert.All(table.FilteredRows, r => Assert.True(Convert.ToInt32(r.Fields[1]) > 5));
    }

    private static TableOfSqlResults CreateTable()
    {
        var rows = new List<TableRow>
        {
            new() { Fields = ["abc", 1] },
            new() { Fields = ["abcd", 10] },
            new() { Fields = ["xyz", 20] },
        };

        var table = new TableOfSqlResults
        {
            Headers = ["C1", "C2"],
            DataTypeNames = ["varchar", "int"],
            TypeCodes = [TypeCode.String, TypeCode.Int32],
            Rows = rows,
            FilteredRows = new BulkObservableCollection<TableRow>(rows),
        };

        table.RebuildRowIndexMap();
        return table;
    }
}
