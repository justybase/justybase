using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JustyBase.Views.Tools;

namespace JustyBase.HeadlessTests;

public sealed class SqlResultsGridSelectionHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task ResultsGrid_DoesNotSelectFirstRowOnShowOrRebind() => RunWithAsyncUi(async () =>
    {
        var view = SqlResultsFindFlowHeadlessTests.CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        AssertNoSelection(view, "initial show");

        vm.GridVisible = false;
        vm.ViewBridge?.SuspendGridBinding();
        vm.GridCollectionView = new Avalonia.Collections.DataGridCollectionView(vm.CurrentResultsTable.FilteredRows);
        vm.ViewBridge?.ResumeGridBinding(clearSelection: true);
        vm.GridVisible = true;
        Dispatcher.UIThread.RunJobs();

        AssertNoSelection(view, "result rebind");
        window.Close();
        await Task.CompletedTask;
    });

    private static void AssertNoSelection(SqlResultsView view, string phase)
    {
        var grid = view.ResultDataGrid;
        var firstRow = grid.GetVisualDescendants()
            .OfType<DataGridRow>()
            .FirstOrDefault(row => ReferenceEquals(row.DataContext, view.CurrentResultsTable.Rows[0]));

        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);
        Assert.Empty(grid.SelectedItems);
        Assert.Empty(grid.SelectedCells);
        Assert.False(firstRow?.IsSelected ?? false, $"The first row is selected after {phase}.");
    }

    private Task<bool> RunWithAsyncUi(Func<Task> action)
    {
        Assert.NotNull(Session);
        return Session!.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
    }
}
