using System.Globalization;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class SummaryRowServiceTests
{
    private static TableOfSqlResults CreateTable(IEnumerable<object[]> fields)
    {
        var table = new TableOfSqlResults();
        table.Headers.Add("Value");
        table.TypeCodes = [TypeCode.Int32];
        foreach (object[] values in fields)
        {
            table.Rows.Add(new TableRow { Fields = values });
        }
        table.FilteredRows.AddRange(table.Rows);
        return table;
    }

    [Fact]
    public void CalculateSummaryValue_UsesExplicitRowSubset_NotFilteredRows()
    {
        var table = CreateTable([[1], [2], [3], [4], [5]]);
        IReadOnlyList<TableRow> subset = [table.Rows[0], table.Rows[1], table.Rows[2]];

        var service = new SummaryRowService();

        string sum = service.CalculateSummaryValue(table, subset, 0, ColumnSummaryType.Sum);
        string count = service.CalculateSummaryValue(table, subset, 0, ColumnSummaryType.Count);

        Assert.Equal($"Σ {6.ToString("N6", CultureInfo.CurrentCulture)}", sum);
        Assert.Equal("# 3", count);
    }

    [Fact]
    public void CalculateSummaryValue_EmptySubset_ReturnsEmpty()
    {
        var table = CreateTable([[1], [2]]);

        var service = new SummaryRowService();

        Assert.Equal(string.Empty, service.CalculateSummaryValue(table, [], 0, ColumnSummaryType.Sum));
        Assert.Equal(string.Empty, service.GetAllStatsTooltip(table, [], 0));
    }

    [Fact]
    public void GetAllStatsTooltip_UsesExplicitRowSubset()
    {
        var table = CreateTable([[1], [2], [3], [4], [5]]);
        IReadOnlyList<TableRow> subset = [table.Rows[3], table.Rows[4]];

        var service = new SummaryRowService();

        string tooltip = service.GetAllStatsTooltip(table, subset, 0);

        Assert.Contains($"Count: {2.ToString("N0", CultureInfo.CurrentCulture)}", tooltip);
        Assert.Contains($"Sum: {9.ToString("N6", CultureInfo.CurrentCulture)}", tooltip);
        Assert.Contains($"Average: {4.5.ToString("N6", CultureInfo.CurrentCulture)}", tooltip);
    }

    [Fact]
    public void CalculateSummaryValue_FullRows_MatchesFilteredRowsBehavior()
    {
        var table = CreateTable([[1], [2], [3]]);

        var service = new SummaryRowService();

        string fromSubset = service.CalculateSummaryValue(table, table.Rows, 0, ColumnSummaryType.Sum);
        string fromTable = service.CalculateSummaryValue(table, 0, ColumnSummaryType.Sum);

        Assert.Equal(fromTable, fromSubset);
    }
}
