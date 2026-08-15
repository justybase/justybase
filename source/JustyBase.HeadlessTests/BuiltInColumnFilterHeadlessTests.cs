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
        var table = CreateResultTable();
        var grid = new DataGrid
        {
            ItemsSource = new DataGridCollectionView(table.FilteredRows),
            AutoGenerateColumns = false,
            IsReadOnly = true,
            RowHeight = 22
        };
        var converters = new List<Avalonia.Data.Converters.IValueConverter>();
        for (int i = 0; i < table.Headers.Count; i++)
        {
            grid.Columns.Add(ResultGridColumnFactory.CreateColumn(table, i, null!, new Dictionary<string, int>(), converters));
        }
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var column = grid.Columns[1];
        Assert.IsType<DataGridDistinctValueFilterFlyout>(column.FilterFlyout);
        Assert.True(column.ShowFilterButton);

        var header = grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, column.Header));
        Assert.True(header is not null,
            $"column header not found; headers: {string.Join(", ", grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Select(h => $"'{h.Content}'"))}");

        header!.TryShowFilterFlyout();
        Dispatcher.UIThread.RunJobs();

        var flyout = (DataGridDistinctValueFilterFlyout)column.FilterFlyout!;
        Assert.Null(flyout.LastError);
        Assert.NotNull(flyout.Context);
        Assert.Equal(100, flyout.Context!.Options.Count);
        Assert.All(flyout.Context.Options, o => Assert.Equal(1, o.Count));
        Assert.True(flyout.Context.Options.All(o => !o.IsSelected));
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
}
