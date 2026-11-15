using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Tools;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Regression coverage for grouping a result grid. Group headers must show the real
/// column value and each distinct value must be its own group: the previous
/// <see cref="DataGridPathGroupDescription"/> path resolves <c>Fields[index]</c> through
/// reflection that the Native AOT build trims, so every row collapsed into one group
/// labelled <c>&lt;NULL&gt;</c>.
/// </summary>
public sealed class SqlResultsGroupingHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task GroupByColumn_ShowsRealGroupKeysInOrder()
    {
        Assert.NotNull(Session);
        return Session!.Dispatch(async () =>
        {
            var view = SqlResultsFindFlowHeadlessTests.CreateView(out SqlResultsViewModel vm);
            using var vmLease = vm;
            var window = new Window { Width = 800, Height = 500, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Column "B" holds a distinct integer per row, so grouping must yield one
            // group per value, ordered ascending by the grouped column.
            vm.ToggleGroupByColumn("B", new ResultGridGroupingService());
            Dispatcher.UIThread.RunJobs();

            var groups = vm.GridCollectionView.Groups!
                .Cast<DataGridCollectionViewGroup>()
                .Select(group => (Key: group.Key, Items: group.ItemCount))
                .ToList();

            Assert.NotEmpty(groups);
            Assert.DoesNotContain(groups, static g => g.Key is null);
            Assert.DoesNotContain(groups, static g => g.Key!.ToString()!.StartsWith("<NULL>", StringComparison.Ordinal));
            Assert.Equal(100, groups.Count);
            Assert.All(groups, static g => Assert.Equal(1, g.Items));
            Assert.Equal(
                Enumerable.Range(0, 100).Select(static i => (object)i).ToList(),
                groups.Select(static g => g.Key).ToList());

            window.Close();
            await Task.CompletedTask;
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public Task GroupByFirstColumn_ShowsRealGroupKeys()
    {
        Assert.NotNull(Session);
        return Session!.Dispatch(async () =>
        {
            var view = SqlResultsFindFlowHeadlessTests.CreateView(out SqlResultsViewModel vm);
            using var vmLease = vm;
            var window = new Window { Width = 800, Height = 500, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Column "A" holds 100 distinct strings; the old path-based grouping put
            // them all in one NULL group.
            vm.ToggleGroupByColumn("A", new ResultGridGroupingService());
            Dispatcher.UIThread.RunJobs();

            var groups = vm.GridCollectionView.Groups!
                .Cast<DataGridCollectionViewGroup>()
                .Select(group => group.Key)
                .ToList();

            Assert.Equal(100, groups.Count);
            Assert.All(groups, static key => Assert.IsType<string>(key));
            Assert.DoesNotContain(groups, static k => k!.ToString()!.StartsWith("<NULL>", StringComparison.Ordinal));

            window.Close();
            await Task.CompletedTask;
            return true;
        }, CancellationToken.None);
    }
}
