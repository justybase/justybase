using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Experimental coverage for the built-in distinct-value column filter
/// (ProDataGrid #318) wired into the real results view.
/// </summary>
public sealed class BuiltInColumnFilterHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task FilteringModel_InDescriptor_FiltersCollectionView() => RunOnUi(() =>
    {
        var view = SqlResultsFindFlowHeadlessTests.CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(100, vm.GridCollectionView.Cast<object>().Count());

        vm.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.In,
            propertyPath: "Fields[1]",
            values: new object[] { 1, 2, 3 }));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, vm.GridCollectionView.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task FilteringModel_ReappliesToReplacedCollectionView() => RunOnUi(() =>
    {
        var view = SqlResultsFindFlowHeadlessTests.CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.In,
            propertyPath: "Fields[1]",
            values: new object[] { 1, 2, 3 }));
        Dispatcher.UIThread.RunJobs();

        // Mimic MakeSearch: detach, swap the collection view, re-attach.
        vm.ViewBridge!.SuspendGridBinding();
        vm.GridCollectionView = new DataGridCollectionView(vm.CurrentResultsTable.FilteredRows);
        vm.ViewBridge.ResumeGridBinding();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, vm.GridCollectionView.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task FilteringModel_Remove_RestoresCollectionView() => RunOnUi(() =>
    {
        var view = SqlResultsFindFlowHeadlessTests.CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.In,
            propertyPath: "Fields[1]",
            values: new object[] { 1, 2, 3 }));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, vm.GridCollectionView.Cast<object>().Count());

        vm.FilteringModel.Remove("col1");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(100, vm.GridCollectionView.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task DistinctFilterFlyout_WithAccessor_BuildsOptionsAndCounts() => RunOnUi(() =>
    {
        var view = SqlResultsFindFlowHeadlessTests.CreateView(out _);
        var grid = view.ResultDataGrid;
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var column = grid.Columns[1];
        Assert.IsType<CascadingDistinctValueFilterFlyout>(column.FilterFlyout);
        Assert.True(column.ShowFilterButton);

        var flyout = (CascadingDistinctValueFilterFlyout)column.FilterFlyout!;
        flyout.ShowAt(grid);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(flyout.LastError);
        Assert.NotNull(flyout.ContentTemplate);
        Assert.NotNull(flyout.Context);
        Assert.Equal(100, flyout.Context!.Options.Count);
        Assert.All(flyout.Context.Options, o => Assert.Equal(1, o.Count));
        Assert.True(flyout.Context.Options.All(o => !o.IsSelected));
        window.Close();
    });

    [Fact]
    public Task DistinctFilterFlyout_UsesCurrentFilteredViewForOptions() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col0",
            @operator: FilteringOperator.In,
            propertyPath: "Fields[0]",
            values: new object[] { "Name1", "Name2" }));
        Dispatcher.UIThread.RunJobs();

        var column = grid.Columns[1];
        FindHeader(grid, column).TryShowFilterFlyout();
        Dispatcher.UIThread.RunJobs();

        var flyout = (CascadingDistinctValueFilterFlyout)column.FilterFlyout!;
        Assert.NotNull(flyout.Context);
        Assert.Equal(2, flyout.Context!.Options.Count);
        Assert.Contains(flyout.Context.Options, option => option.Display == "1");
        Assert.Contains(flyout.Context.Options, option => option.Display == "2");
        window.Close();
    });

    [Fact]
    public Task DistinctFilterFlyout_ClearAll_RemovesColumnDescriptor() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var column = grid.Columns[0];
        FindHeader(grid, column).TryShowFilterFlyout();
        Dispatcher.UIThread.RunJobs();
        var flyout = (CascadingDistinctValueFilterFlyout)column.FilterFlyout!;

        flyout.Context!.Options[0].IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(grid.FilteringModel.Descriptors);
        Assert.Single(grid.ItemsSource.Cast<object>());

        flyout.Context.ClearAllCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(grid.FilteringModel.Descriptors);
        Assert.Equal(100, grid.ItemsSource.Cast<object>().Count());
        window.Close();
    });

    [Fact]
    public Task DistinctFilterFlyout_SearchText_FiltersVisibleOptions() => RunOnUi(() =>
    {
        var table = CreateResultTable();
        var grid = CreateFilterGrid(table);
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var column = grid.Columns[0];
        FindHeader(grid, column).TryShowFilterFlyout();
        Dispatcher.UIThread.RunJobs();
        var flyout = (CascadingDistinctValueFilterFlyout)column.FilterFlyout!;

        flyout.Context!.SearchText = "Name1";

        Assert.Equal(11, flyout.Context.Options.Count);
        Assert.All(flyout.Context.Options, option =>
            Assert.Contains("Name1", option.Display, StringComparison.OrdinalIgnoreCase));
        window.Close();
    });

    [Fact]
    public Task DistinctFilterFlyout_WithoutAccessor_SetsLastError() => RunOnUi(() =>
    {
        var grid = new DataGrid
        {
            ItemsSource = new DataGridCollectionView(Array.Empty<object>()),
            AutoGenerateColumns = false,
            IsReadOnly = true
        };
        var column = new DataGridTextColumn { Header = "A", Binding = new Avalonia.Data.Binding("Fields[0]") };
        grid.Columns.Add(column);

        var flyout = new DataGridDistinctValueFilterFlyout { Placement = PlacementMode.Bottom };
        column.FilterFlyout = flyout;
        column.ShowFilterButton = true;

        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, "A"));
        Assert.True(header is not null, "column header not found");

        header!.TryShowFilterFlyout();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(flyout.LastError);
        window.Close();
    });

    [Fact]
    public Task FilteringChanged_UpdatesRowsLoadingMessage() => RunOnUi(() =>
    {
        var view = SqlResultsFindFlowHeadlessTests.CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rowsText = view.FindControl<TextBlock>("rowsLoadingMessage");
        Assert.NotNull(rowsText);

        vm.FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: "col1",
            @operator: FilteringOperator.In,
            propertyPath: "Fields[1]",
            values: new object[] { 1, 2, 3 }));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("3 rows", rowsText!.Text);
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

    private static DataGridColumnHeader FindHeader(DataGrid grid, DataGridColumn column)
    {
        var header = grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, column.Header));
        Assert.NotNull(header);
        return header!;
    }
}
