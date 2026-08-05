using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public class ResultGridStatsServiceTests
{
    [Fact]
    public void CalculateStats_FromRawData_WithMixedTypes_CalculatesCorrectly()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (0, 0, 100, TypeCode.Int32),
            (1, 0, "text", TypeCode.String),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 100, "text" } },
            typeCodes: [TypeCode.Int32, TypeCode.String]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(2, result.SelectedCount);
        Assert.Equal(100m, result.Sum);
        Assert.Equal(2, result.NotNullCount);
        Assert.Equal(2, result.DistinctCount);
        Assert.Equal(100m, result.Min);
        Assert.Equal(100m, result.Max);
    }

    [Fact]
    public void CalculateStats_FromRawData_WithOutOfRangeIndices_IgnoresInvalid()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (-1, 0, 100, TypeCode.Int32),
            (0, 0, 50, TypeCode.Int32),
            (10, 0, 200, TypeCode.Int32),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 50 } },
            typeCodes: [TypeCode.Int32]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(50m, result.Sum);
    }

    [Fact]
    public void CalculateStats_FromRawData_WithDuplicateValues_CalculatesDistinctCorrectly()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (0, 0, 10, TypeCode.Int32),
            (0, 1, 10, TypeCode.Int32),
            (0, 2, 20, TypeCode.Int32),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 10 }, new object[] { 10 }, new object[] { 20 } },
            typeCodes: [TypeCode.Int32]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(2, result.DistinctCount);
        Assert.Equal(40m, result.Sum);
    }

    [Fact]
    public void CalculateStats_WithNullResultsTable_HandlesGracefully()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>();

        var result = service.CalculateStatsFromRawData(cellData, null);

        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(0m, result.Sum);
    }

    [Fact]
    public void CalculateStatsFromRawData_WithValidNumericCells_CalculatesStatsCorrectly()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (0, 0, 10, TypeCode.Int32),
            (1, 0, 20, TypeCode.Int32),
            (2, 0, 30, TypeCode.Int32),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 10, 20, 30 } },
            typeCodes: [TypeCode.Int32, TypeCode.Int32, TypeCode.Int32]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(60m, result.Sum);
        Assert.Equal(3, result.NotNullCount);
        Assert.Equal(3, result.DistinctCount);
        Assert.Equal(10m, result.Min);
        Assert.Equal(30m, result.Max);
    }

    [Fact]
    public void CalculateStatsFromRawData_WithOutOfRangeIndices_IgnoresInvalid()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (-1, 0, 100, TypeCode.Int32),
            (0, 0, 50, TypeCode.Int32),
            (10, 0, 200, TypeCode.Int32),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 50 } },
            typeCodes: [TypeCode.Int32]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(50m, result.Sum);
    }

    [Fact]
    public void CalculateStatsFromRawData_WithDuplicateValues_CalculatesDistinctCorrectly()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (0, 0, 10, TypeCode.Int32),
            (0, 1, 10, TypeCode.Int32),
            (0, 2, 20, TypeCode.Int32),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 10 }, new object[] { 10 }, new object[] { 20 } },
            typeCodes: [TypeCode.Int32]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(2, result.DistinctCount);
        Assert.Equal(40m, result.Sum);
    }

    [Fact]
    public void CalculateStats_FromRawData_WithValidNumericCells_CalculatesStatsCorrectly()
    {
        var service = new ResultGridStatsService();
        var cellData = new List<(int ColumnIndex, int RowIndex, object? Value, TypeCode TypeCode)>
        {
            (0, 0, 10, TypeCode.Int32),
            (0, 1, 20, TypeCode.Int32),
            (0, 2, 30, TypeCode.Int32),
        };
        var resultsTable = CreateTestResultsTableWithTypedValues(
            rows: new object[][] { new object[] { 10 }, new object[] { 20 }, new object[] { 30 } },
            typeCodes: [TypeCode.Int32]);

        var result = service.CalculateStatsFromRawData(cellData, resultsTable);

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(60m, result.Sum);
        Assert.Equal(3, result.NotNullCount);
        Assert.Equal(3, result.DistinctCount);
        Assert.Equal(10m, result.Min);
        Assert.Equal(30m, result.Max);
    }

    private static TableOfSqlResults CreateTestResultsTable(
        List<string[]>? rows = null,
        List<TypeCode>? typeCodes = null)
    {
        rows ??= [];
        typeCodes ??= [TypeCode.String];

        var filteredRows = rows.Select(r => new TableRow { Fields = r.Cast<object>().ToArray() }).ToList();
        var allRows = new List<TableRow>(filteredRows);

        return new TableOfSqlResults
        {
            Rows = allRows,
            FilteredRows = new BulkObservableCollection<TableRow>(filteredRows),
            Headers = rows.Count > 0 ? rows[0].Select((_, i) => $"Col{i}").ToList() : [],
            TypeCodes = typeCodes
        };
    }

    private static TableOfSqlResults CreateTestResultsTableWithTypedValues(
        object[][] rows,
        List<TypeCode>? typeCodes = null)
    {
        typeCodes ??= [TypeCode.Object];

        var filteredRows = rows.Select(r => new TableRow { Fields = r }).ToList();
        var allRows = new List<TableRow>(filteredRows);

        return new TableOfSqlResults
        {
            Rows = allRows,
            FilteredRows = new BulkObservableCollection<TableRow>(filteredRows),
            Headers = rows.Length > 0 ? rows[0].Select((_, i) => $"Col{i}").ToList() : [],
            TypeCodes = typeCodes
        };
    }
}
