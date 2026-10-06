using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Headless;
using Avalonia.Threading;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Locks down how ProDataGrid interprets the comparison operators used by the Excel-like
/// value-filter section of the results-grid column filter.
/// </summary>
public sealed class ValueFilterOperatorHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task GreaterThan_WithValue_FiltersNumericColumn() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.GreaterThan,
            propertyPath: "Fields[1]",
            value: 90));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(9, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task GreaterThanOrEqual_WithValue_FiltersNumericColumn() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.GreaterThanOrEqual,
            propertyPath: "Fields[1]",
            value: 90));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(10, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task LessThan_WithValue_FiltersNumericColumn() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.LessThan,
            propertyPath: "Fields[1]",
            value: 5));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task Between_WithTwoValues_FiltersNumericColumn() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.Between,
            propertyPath: "Fields[1]",
            values: new object[] { 10, 19 }));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(10, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task Contains_WithValue_FiltersTextColumn() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col0",
            @operator: FilteringOperator.Contains,
            propertyPath: "Fields[0]",
            value: "Name1"));
        Dispatcher.UIThread.RunJobs();

        // Name1 plus Name10..Name19.
        Assert.Equal(11, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task NotEquals_WithValue_FiltersTextColumn() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col0",
            @operator: FilteringOperator.NotEquals,
            propertyPath: "Fields[0]",
            value: "Name1"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(99, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    private static TableOfSqlResults CreateResultTable()
    {
        var table = new TableOfSqlResults();
        table.Headers.Add("A");
        table.Headers.Add("B");
        table.TypeCodes = [TypeCode.String, TypeCode.Int32];
        for (int i = 0; i < 100; i++)
        {
            table.Rows.Add(new TableRow { Fields = [$"Name{i}", i] });
        }
        table.FilteredRows.AddRange(table.Rows);
        return table;
    }

    private static DataGrid CreateFilterGrid(TableOfSqlResults table)
    {
        var grid = new DataGrid
        {
            ItemsSource = new DataGridCollectionView(table.FilteredRows),
            AutoGenerateColumns = false,
            IsReadOnly = true,
            RowHeight = 22,
            FilteringModel = new FilteringModel { OwnsViewFilter = true }
        };
        var converters = new List<Avalonia.Data.Converters.IValueConverter>();
        for (int i = 0; i < table.Headers.Count; i++)
        {
            grid.Columns.Add(ResultGridColumnFactory.CreateColumn(
                table,
                i,
                null!,
                new Dictionary<string, int>(),
                converters));
        }

        return grid;
    }
}
